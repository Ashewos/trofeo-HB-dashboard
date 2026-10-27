using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Trofeo.Control;
using Trofeo.ModuleKit;
using Trofeo.Settings.Services;
using Trofeo.Settings.ViewModels.Fields;

namespace Trofeo.Settings.Tests;

/// <summary>
/// Les champs de formulaire seuls, sans l'onglet Modules : un contexte de fortune leur tient lieu de disposition. C'est
/// ainsi que l'assistant de première ouverture les construira.
/// </summary>
public sealed class FieldViewModelTests
{
    /// <summary>Des réglages en mémoire ; chaque appel note ce que le champ a demandé.</summary>
    private sealed class FakeFieldContext : IFieldContext
    {
        public string TypeId => "demo";
        public TimeProvider Time { get; } = new FakeTimeProvider();
        public ISecretAccess Secrets { get; } = new FakeSecretAccess();
        public bool CanRunActions { get; set; } = true;
        public JsonElement Settings { get; set; } = JsonDocument.Parse("{}").RootElement.Clone();
        public int Edits { get; private set; }
        public int SecretChanges { get; private set; }
        public List<string> Actions { get; } = [];
        public Func<string, string, Task<IReadOnlyList<LookupResult>>> OnLookup { get; set; } = (_, _) => Task.FromResult<IReadOnlyList<LookupResult>>([]);
        public Func<string, Task<ActionResult>> OnAction { get; set; } = _ => Task.FromResult(new ActionResult(true, "Fait."));

        public Task EditAsync(Func<JsonElement, JsonElement> change)
        {
            Settings = change(Settings);
            Edits++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LookupResult>> LookupAsync(string fieldKey, string query) => OnLookup(fieldKey, query);

        public Task<ActionResult> RunActionAsync(string actionKey)
        {
            Actions.Add(actionKey);
            return OnAction(actionKey);
        }

        public Task SecretChangedAsync()
        {
            SecretChanges++;
            return Task.CompletedTask;
        }
    }

    private static ModuleForm Form(FakeFieldContext context, params SettingField[] fields) =>
        FieldFactory.CreateForm(new SettingsSchema(fields), context);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static SettingOption[] Options(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new SettingOption("v" + i, "Option " + i))];

    [Fact]
    public void Form_IsBuiltFromASchemaAndAContext_Alone()
    {
        var context = new FakeFieldContext();

        var form = Form(context,
            new ChoiceField("a", "A", Options(2), "v1"),
            new ToggleField("b", "B", true),
            new TextField("c", "C"),
            new ListField("d", "D", Options(3), ["v1"]),
            new SecretField("e", "E"),
            new LookupField("f", "F"),
            new ActionField("g", "G"));

        Assert.Equal(
            [typeof(ChoiceFieldViewModel), typeof(ToggleFieldViewModel), typeof(TextFieldViewModel), typeof(ListFieldViewModel),
             typeof(SecretFieldViewModel), typeof(LookupFieldViewModel), typeof(ActionFieldViewModel)],
            form.Fields.Select(f => f.GetType()));
        Assert.Equal(7, form.VisibleFields.Count);
        Assert.False(form.IsEmpty);
    }

    [Fact]
    public void Form_OnlyKeepsTheWantedFields_ButVisibilityStillReadsTheWholeSchema()
    {
        var context = new FakeFieldContext { Settings = Json("""{"source":"unifi"}""") };
        var schema = new SettingsSchema(
        [
            new ChoiceField("source", "Source", Options(3), "v1"),
            new TextField("host", "Hôte") { VisibleWhen = new FieldCondition("source", "unifi") },
        ]);

        var form = FieldFactory.CreateForm(schema, context, only: field => field.Key == "host");

        Assert.Equal(["host"], form.VisibleFields.Select(f => f.Key));    // « source » vaut unifi, lu dans les réglages
    }

    [Fact]
    public void Form_EmptySchema_IsEmpty()
    {
        var form = Form(new FakeFieldContext());

        Assert.True(form.IsEmpty);
        Assert.Empty(form.VisibleFields);
    }

    [Fact]
    public void Choice_UpToThreeOptions_IsSegmented_MoreIsAList()
    {
        var form = Form(new FakeFieldContext(),
            new ChoiceField("three", "Trois", Options(3), "v1"),
            new ChoiceField("four", "Quatre", Options(4), "v1"));

        var three = form.Field<ChoiceFieldViewModel>("three");
        var four = form.Field<ChoiceFieldViewModel>("four");
        Assert.True(three.IsSegmented);
        Assert.False(three.IsDropDown);
        Assert.False(four.IsSegmented);
        Assert.True(four.IsDropDown);
    }

    [Fact]
    public async Task Choice_DropDown_SelectedOptionWritesWhenSet()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new ChoiceField("four", "Quatre", Options(4), "v1"));
        var choice = form.Field<ChoiceFieldViewModel>("four");
        Assert.Equal("v1", choice.SelectedOption!.Value);

        choice.SelectedOption = choice.Options[2];
        await choice.Pending;

        Assert.Equal("v3", SettingsDocument.GetString(context.Settings, "four"));
    }

    [Fact]
    public void Choice_UnknownStoredValue_SelectsNothing()
    {
        var context = new FakeFieldContext { Settings = Json("""{"a":"zzz"}""") };
        var form = Form(context, new ChoiceField("a", "A", Options(2), "v1"));

        Assert.All(form.Field<ChoiceFieldViewModel>("a").Options, o => Assert.False(o.IsSelected));
    }

    [Fact]
    public async Task Choice_WritesNestedKeys()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new ChoiceField("net.mode", "Mode", Options(2), "v1"));
        var choice = form.Field<ChoiceFieldViewModel>("net.mode");

        await choice.SelectAsync(choice.Options[1]);

        Assert.Equal("v2", context.Settings.GetProperty("net").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Toggle_ReadsItsDefault_AndWrites()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new ToggleField("on", "Oui", true));
        var toggle = form.Field<ToggleFieldViewModel>("on");
        Assert.True(toggle.IsOn);

        await toggle.SetAsync(false);

        Assert.False(toggle.IsOn);
        Assert.False(context.Settings.GetProperty("on").GetBoolean());
        await toggle.SetAsync(false);
        Assert.Equal(1, context.Edits);                                   // rien ne change : rien n'est demandé
    }

    [Fact]
    public async Task Text_IsBoundedToMaxLength_AndTrimmed()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new TextField("name", "Nom", MaxLength: 5) { Placeholder = "Ton nom" });
        var text = form.Field<TextFieldViewModel>("name");
        Assert.Equal(5, text.MaxLength);
        Assert.Equal("Ton nom", text.Placeholder);

        text.Text = "  abcdefgh ";
        await text.CommitAsync();

        Assert.Equal("ab", SettingsDocument.GetString(context.Settings, "name")?[..2]);
        Assert.Equal(5, SettingsDocument.GetString(context.Settings, "name")!.Length);
        Assert.Equal(text.Text, SettingsDocument.GetString(context.Settings, "name"));
    }

    [Fact]
    public async Task Text_UnchangedCommit_AsksForNothing()
    {
        var context = new FakeFieldContext { Settings = Json("""{"name":"Salon"}""") };
        var form = Form(context, new TextField("name", "Nom"));
        var text = form.Field<TextFieldViewModel>("name");

        await text.CommitAsync();

        Assert.Equal(0, context.Edits);
    }

    [Fact]
    public async Task Text_Refresh_TakesTheNewValue_ExceptOverADraft()
    {
        var context = new FakeFieldContext { Settings = Json("""{"name":"Salon"}""") };
        var form = Form(context, new TextField("name", "Nom"));
        var text = form.Field<TextFieldViewModel>("name");

        context.Settings = Json("""{"name":"Chambre"}""");                // annulation, rechargement…
        form.Refresh();
        Assert.Equal("Chambre", text.Text);

        text.Text = "Brouillon";
        context.Settings = Json("""{"name":"Cuisine"}""");
        form.Refresh();
        Assert.Equal("Brouillon", text.Text);

        await form.CommitPendingAsync();
        Assert.Equal("Brouillon", SettingsDocument.GetString(context.Settings, "name"));
    }

    [Fact]
    public async Task Secret_Presence_FollowsTheVault()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new SecretField("token", "Jeton"));
        var secret = form.Field<SecretFieldViewModel>("token");
        Assert.False(secret.IsPresent);
        Assert.Equal("Absente", secret.StatusText);

        secret.Edit();
        Assert.True(secret.IsEditing);
        secret.Input = "x";
        secret.Cancel();                                                  // abandonner n'écrit rien et efface la saisie
        Assert.False(secret.IsEditing);
        Assert.Equal("", secret.Input);
        Assert.False(context.Secrets.Has("demo", "token"));

        secret.Edit();
        secret.Input = "x";
        await secret.SaveAsync();
        Assert.True(secret.IsPresent);
        Assert.Equal("Enregistrée", secret.StatusText);
        Assert.Equal(1, context.SecretChanges);
    }

    [Fact]
    public async Task Secret_BlankInput_IsNotSaved()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new SecretField("token", "Jeton"));
        var secret = form.Field<SecretFieldViewModel>("token");

        secret.Edit();
        secret.Input = "   ";
        Assert.False(secret.CanSave);
        await secret.SaveAsync();

        Assert.False(context.Secrets.Has("demo", "token"));
        Assert.Equal(0, context.SecretChanges);
    }

    [Fact]
    public void Lookup_CurrentText_ComesFromTheStoredValue()
    {
        var context = new FakeFieldContext();
        var withEmpty = new LookupField("city", "Ville") { EmptyLabel = "Aucune" };
        var form = Form(context, withEmpty, new LookupField("other", "Autre") { LabelPath = "title" });
        var city = form.Field<LookupFieldViewModel>("city");
        var other = form.Field<LookupFieldViewModel>("other");

        Assert.Equal("Aucune", city.CurrentText);                         // absente : le libellé du vide
        Assert.False(city.HasValue);
        Assert.Equal("", other.CurrentText);                              // absente et sans libellé du vide : rien

        context.Settings = Json("""{"city":"Lyon","other":{"title":"Marseille","name":"ignoré"}}""");
        form.Refresh();
        Assert.Equal("Lyon", city.CurrentText);                           // une chaîne se montre telle quelle
        Assert.Equal("Marseille", other.CurrentText);                     // un objet se lit par LabelPath
        Assert.True(city.HasValue);
    }

    [Fact]
    public void Lookup_NonSearchable_LoadsOnlyWhenShown()
    {
        var calls = 0;
        var context = new FakeFieldContext
        {
            OnLookup = (_, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<LookupResult>>([]);
            },
        };
        var schema = new SettingsSchema(
        [
            new ChoiceField("mode", "Mode", Options(2), "v1"),
            new LookupField("gpu", "Carte", Searchable: false) { VisibleWhen = new FieldCondition("mode", "v2") },
        ]);

        var form = FieldFactory.CreateForm(schema, context);
        Assert.Equal(0, calls);                                           // cachée : rien n'est demandé

        context.Settings = Json("""{"mode":"v2"}""");
        form.Refresh();
        Assert.Equal(1, calls);

        form.Refresh();
        Assert.Equal(1, calls);                                           // une seule fois
    }

    [Fact]
    public async Task Lookup_Searchable_AsksNothingForAnEmptyQuery_AndShowsFiveResultsAtMost()
    {
        var context = new FakeFieldContext();
        var asked = new List<string>();
        context.OnLookup = (_, query) =>
        {
            asked.Add(query);
            return Task.FromResult<IReadOnlyList<LookupResult>>(
                [.. Enumerable.Range(1, 8).Select(i => new LookupResult("Ville " + i, Json("\"v" + i + "\"")))]);
        };
        var form = Form(context, new LookupField("city", "Ville"));
        var city = form.Field<LookupFieldViewModel>("city");
        var time = (FakeTimeProvider)context.Time;

        city.Query = "   ";
        time.Advance(TimeSpan.FromMilliseconds(400));
        await city.SearchTask;
        Assert.Empty(asked);

        city.Query = " Ly ";
        time.Advance(TimeSpan.FromMilliseconds(400));
        await city.SearchTask;
        Assert.Equal(["Ly"], asked);                                      // la requête est rognée
        Assert.Equal(5, city.Rows.Count);

        city.Query = "";                                                  // effacer la requête referme les résultats
        await city.SearchTask;
        Assert.Empty(city.Rows);
    }

    [Fact]
    public async Task Lookup_StaleAnswer_IsIgnored()
    {
        var context = new FakeFieldContext();
        var slow = new TaskCompletionSource<IReadOnlyList<LookupResult>>();
        context.OnLookup = (_, query) => query == "a"
            ? slow.Task
            : Task.FromResult<IReadOnlyList<LookupResult>>([new LookupResult("Réponse à ab", Json("\"ab\""))]);
        var form = Form(context, new LookupField("city", "Ville"));
        var city = form.Field<LookupFieldViewModel>("city");
        var time = (FakeTimeProvider)context.Time;

        city.Query = "a";
        time.Advance(TimeSpan.FromMilliseconds(400));
        var first = city.SearchTask;                                      // la recherche de « a » est partie, sans réponse

        city.Query = "ab";
        time.Advance(TimeSpan.FromMilliseconds(400));
        await city.SearchTask;
        slow.SetResult([new LookupResult("Réponse à a", Json("\"a\""))]); // la vieille réponse arrive après
        await first;

        Assert.Equal(["Réponse à ab"], city.Rows.Select(r => r.Label));
    }

    [Fact]
    public async Task Action_WithoutConfirm_RunsAtOnce()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new ActionField("go", "Lancer"));
        var action = form.Field<ActionFieldViewModel>("go");

        await action.RequestAsync();

        Assert.Equal(["go"], context.Actions);
        Assert.False(action.IsConfirming);
        Assert.Equal("Fait.", action.Message);
        Assert.True(action.IsOk);
    }

    [Fact]
    public async Task Action_RunsAfterThePendingTextIsCommitted()
    {
        var context = new FakeFieldContext();
        string? seen = null;
        context.OnAction = _ =>
        {
            seen = SettingsDocument.GetString(context.Settings, "host");
            return Task.FromResult(new ActionResult(true, "Fait."));
        };
        var form = Form(context, new TextField("host", "Hôte"), new ActionField("go", "Lancer"));
        form.Field<TextFieldViewModel>("host").Text = "10.0.0.1";

        await form.Field<ActionFieldViewModel>("go").RequestAsync();

        Assert.Equal("10.0.0.1", seen);
    }

    [Fact]
    public async Task Action_ANewRun_ClearsThePreviousMessage()
    {
        var context = new FakeFieldContext();
        var form = Form(context, new ActionField("go", "Lancer"));
        var action = form.Field<ActionFieldViewModel>("go");
        await action.RequestAsync();
        Assert.True(action.HasMessage);

        var gate = new TaskCompletionSource<ActionResult>();
        context.OnAction = _ => gate.Task;
        var second = action.RequestAsync();

        Assert.False(action.HasMessage);
        gate.SetResult(new ActionResult(false, "Raté."));
        await second;
        Assert.Equal("Raté.", action.Message);
    }

    [Fact]
    public void Visibility_FollowsTheConditionAndTheDefaults()
    {
        var context = new FakeFieldContext();
        var schema = new SettingsSchema(
        [
            new ToggleField("advanced", "Avancé", false),
            new TextField("extra", "Extra") { VisibleWhen = new FieldCondition("advanced", "true") },
        ]);
        var form = FieldFactory.CreateForm(schema, context);
        Assert.Equal(["advanced"], form.VisibleFields.Select(f => f.Key));

        context.Settings = Json("""{"advanced":true}""");
        form.Refresh();

        Assert.Equal(["advanced", "extra"], form.VisibleFields.Select(f => f.Key));
    }
}

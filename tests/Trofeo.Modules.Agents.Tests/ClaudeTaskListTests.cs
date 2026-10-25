namespace Trofeo.Modules.Agents.Tests;

public sealed class ClaudeTaskListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-tasks-" + Guid.NewGuid().ToString("N"));

    public ClaudeTaskListTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string fileName, string status, string subject, string? activeForm = null)
    {
        var active = activeForm is null ? "" : "\"activeForm\":\"" + activeForm + "\",";
        File.WriteAllText(Path.Combine(_dir, fileName),
            $$"""{"id":"{{Path.GetFileNameWithoutExtension(fileName)}}","subject":"{{subject}}","description":"…",{{active}}"status":"{{status}}","blocks":[],"blockedBy":[]}""");
    }

    [Fact]
    public void TaskList_ReadsInNumericOrder_SkipsDeleted()
    {
        Write("2.json", "pending", "Deux");
        Write("10.json", "completed", "Dix");
        Write("1.json", "completed", "Un");
        Write("3.json", "deleted", "Supprimée");
        Write("4.json", "bizarre", "Statut inconnu");
        File.WriteAllText(Path.Combine(_dir, "5.json"), "{ pas du json");
        File.WriteAllText(Path.Combine(_dir, ".lock"), "");
        File.WriteAllText(Path.Combine(_dir, ".highwatermark"), "10");

        var tasks = ClaudeTaskList.Read(_dir);

        Assert.Equal(
            [new AgentTask("Un", AgentTaskState.Completed), new AgentTask("Deux", AgentTaskState.Pending),
             new AgentTask("Dix", AgentTaskState.Completed)],
            tasks);
    }

    [Fact]
    public void TaskList_NonNumericNames_AfterNumbers_Alphabetical()
    {
        Write("zeta.json", "pending", "Z");
        Write("alpha.json", "pending", "A");
        Write("7.json", "pending", "Sept");

        var tasks = ClaudeTaskList.Read(_dir)!;

        Assert.Equal(["Sept", "A", "Z"], tasks.Select(t => t.Text));
    }

    [Fact]
    public void TaskList_InProgress_UsesActiveForm()
    {
        Write("1.json", "in_progress", "Écrire le plan 3", "Écriture du plan 3");
        Write("2.json", "in_progress", "Relire");
        Write("3.json", "pending", "Livrer", "Livraison");

        var tasks = ClaudeTaskList.Read(_dir);

        Assert.Equal(
            [new AgentTask("Écriture du plan 3", AgentTaskState.InProgress), new AgentTask("Relire", AgentTaskState.InProgress),
             new AgentTask("Livrer", AgentTaskState.Pending)],
            tasks);
    }

    [Fact]
    public void TaskList_RealExample_Parses()
    {
        File.WriteAllText(Path.Combine(_dir, "1.json"),
            """{"id":"1","subject":"Écrire le plan 3","description":"…","activeForm":"Écriture du plan 3","status":"in_progress","blocks":[],"blockedBy":[]}""");

        Assert.Equal([new AgentTask("Écriture du plan 3", AgentTaskState.InProgress)], ClaudeTaskList.Read(_dir));
    }

    [Fact]
    public void TaskList_MissingDir_Null() =>
        Assert.Null(ClaudeTaskList.Read(Path.Combine(_dir, "absent")));

    [Fact]
    public void TaskList_EmptyDir_EmptyList()
    {
        var tasks = ClaudeTaskList.Read(_dir);

        Assert.NotNull(tasks);
        Assert.Empty(tasks);
    }
}

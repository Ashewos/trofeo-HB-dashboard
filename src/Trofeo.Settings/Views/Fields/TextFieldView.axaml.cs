using Avalonia.Controls;
using Avalonia.Input;
using Trofeo.Settings.ViewModels.Fields;

namespace Trofeo.Settings.Views.Fields;

public partial class TextFieldView : UserControl
{
    public TextFieldView()
    {
        InitializeComponent();

        // Le texte ne s'enregistre qu'à la perte du focus ou sur Entrée : un seul pas d'annulation, un seul rechargement du service.
        Box.LostFocus += (_, _) => Commit();
        Box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
    }

    private void Commit()
    {
        if (DataContext is TextFieldViewModel { IsDirty: true } field)
            _ = field.CommitAsync();
    }
}

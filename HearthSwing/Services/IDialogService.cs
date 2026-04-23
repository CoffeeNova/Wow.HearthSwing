namespace HearthSwing.Services;

public interface IDialogService
{
    void ShowWarning(string message, string title);

    bool Confirm(string message, string title);
}

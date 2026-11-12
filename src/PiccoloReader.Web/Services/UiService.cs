namespace PiccoloReader.Web.Services;

// Web counterpart of MAUI's DisplayActionSheetAsync / DisplayPromptAsync and the
// delete-confirm overlay. DialogHost renders whatever is pending.
public sealed class UiService
{
    public enum DialogKind { Sheet, Prompt, Confirm, Alert }

    public sealed record Request(DialogKind Kind, string Title, string? Message, IReadOnlyList<string> Options,
        string Initial, string Accept, string Cancel, TaskCompletionSource<string?> Result);

    public Request? Current { get; private set; }

    public string? Toast { get; private set; }

    public event Action? Changed;

    public Task<string?> ChooseAsync(string title, IReadOnlyList<string> options, string cancel)
        => Show(new Request(DialogKind.Sheet, title, null, options, "", "", cancel, new()));

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string initial = "")
        => Show(new Request(DialogKind.Prompt, title, message, [], initial, accept, cancel, new()));

    public async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
        => await Show(new Request(DialogKind.Confirm, title, message, [], "", accept, cancel, new())) is not null;

    // MAUI DisplayAlertAsync with a single button.
    public async Task AlertAsync(string title, string message, string ok)
        => await Show(new Request(DialogKind.Alert, title, message, [], "", ok, ok, new()));

    public async void ShowToast(string text)
    {
        Toast = text;
        Changed?.Invoke();
        await Task.Delay(2500);
        if (Toast == text) { Toast = null; Changed?.Invoke(); }
    }

    public void Resolve(string? value)
    {
        var c = Current;
        Current = null;
        Changed?.Invoke();
        c?.Result.TrySetResult(value);
    }

    private Task<string?> Show(Request r)
    {
        Current?.Result.TrySetResult(null);
        Current = r;
        Changed?.Invoke();
        return r.Result.Task;
    }
}

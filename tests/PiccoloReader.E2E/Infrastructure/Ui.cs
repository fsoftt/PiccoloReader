using System.Drawing;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>
/// Element lookup. App elements are located by MAUI AutomationId only (surfaced by Android as the
/// view's resource-id, or as its content-description for toolbar menu items). The few native
/// dialogs (action sheets, prompts, the system file picker) have no AutomationId, so those are
/// located by their visible text, taken from the app's own AppStrings where possible.
/// </summary>
public static class Ui
{
    private static AndroidDriver D => AppSession.Driver;

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IEnumerable<By> Locators(string automationId)
    {
        yield return MobileBy.AndroidUIAutomator($"new UiSelector().resourceIdMatches(\"(.*:id/)?{automationId}\")");
        yield return MobileBy.AndroidUIAutomator($"new UiSelector().description(\"{automationId}\")");
    }

    public static IReadOnlyList<IWebElement> FindAll(string automationId)
    {
        foreach (var by in Locators(automationId))
        {
            var found = D.FindElements(by);
            if (found.Count > 0)
            {
                return found;
            }
        }

        return Array.Empty<IWebElement>();
    }

    public static IWebElement? TryFind(string automationId) => FindAll(automationId).FirstOrDefault();

    public static bool Exists(string automationId) => TryFind(automationId) is not null;

    public static IWebElement Find(string automationId, int timeoutSeconds = 15)
    {
        IWebElement? element = null;
        Poll(() => (element = TryFind(automationId)) is not null, timeoutSeconds, $"element '{automationId}' to appear");
        return element!;
    }

    public static void WaitGone(string automationId, int timeoutSeconds = 10) =>
        Poll(() => !Exists(automationId), timeoutSeconds, $"element '{automationId}' to disappear");

    public static void Tap(string automationId)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Find(automationId).Click();
                return;
            }
            catch (StaleElementReferenceException) when (attempt < 3)
            {
                // The view was re-created between lookup and click; look it up again.
            }
        }
    }

    public static void SetText(string automationId, string text)
    {
        var element = Find(automationId);
        element.Click();
        element.Clear();
        element.SendKeys(text);
    }

    public static string TextOf(string automationId) => Find(automationId).Text;

    public static Rectangle RectOf(string automationId)
    {
        var element = Find(automationId);
        return new Rectangle(element.Location, element.Size);
    }

    // ---- native / text-based (dialogs, system picker) ----

    public static IWebElement? TryFindText(string text, bool contains = false)
    {
        var selector = contains ? "textContains" : "text";
        return D.FindElements(MobileBy.AndroidUIAutomator($"new UiSelector().{selector}(\"{Esc(text)}\")")).FirstOrDefault();
    }

    public static bool TextExists(string text, bool contains = false) => TryFindText(text, contains) is not null;

    public static IWebElement FindText(string text, int timeoutSeconds = 15, bool contains = false)
    {
        IWebElement? element = null;
        Poll(() => (element = TryFindText(text, contains)) is not null, timeoutSeconds, $"text '{text}' to appear");
        return element!;
    }

    public static void TapText(string text, int timeoutSeconds = 15, bool contains = false) =>
        FindText(text, timeoutSeconds, contains).Click();

    public static IWebElement FindDialogEditText(int timeoutSeconds = 15)
    {
        IWebElement? element = null;
        Poll(
            () => (element = D.FindElements(By.ClassName("android.widget.EditText")).FirstOrDefault()) is not null,
            timeoutSeconds, "a dialog text field");
        return element!;
    }

    public static void Poll(Func<bool> condition, int timeoutSeconds, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (StaleElementReferenceException)
            {
                // retry
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new WebDriverTimeoutException($"Timed out after {timeoutSeconds}s waiting for {what}");
            }

            Thread.Sleep(300);
        }
    }
}

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

    /// <summary>
    /// Text of an element or, for containers (MAUI Border/Grid) that carry the AutomationId while
    /// a child Label carries the text, of its first non-empty descendant.
    /// </summary>
    public static string TextOf(string automationId)
    {
        var element = Find(automationId);
        var own = element.Text;
        if (!string.IsNullOrEmpty(own))
        {
            return own;
        }

        foreach (var node in element.FindElements(By.XPath(".//*")))
        {
            var text = node.Text;
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return element.GetAttribute("content-desc") ?? string.Empty;
    }

    public static bool IsChecked(string automationId) =>
        string.Equals(Find(automationId).GetAttribute("checked"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled(string automationId) =>
        string.Equals(Find(automationId).GetAttribute("enabled"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Center of an element in screen pixels.</summary>
    public static Point CenterOf(string automationId)
    {
        var r = RectOf(automationId);
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

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
        Poll(() =>
        {
            var element = TryFindText(text, contains);
            if (element is null)
            {
                return false;
            }

            element.Click();
            return true;
        }, timeoutSeconds, $"text '{text}' to be tappable");

    public static IWebElement FindDialogEditText(int timeoutSeconds = 15)
    {
        IWebElement? element = null;
        Poll(
            () => (element = D.FindElements(By.ClassName("android.widget.EditText")).LastOrDefault()) is not null,
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

            DismissSystemDialogs();
            Thread.Sleep(300);
        }
    }

    /// <summary>
    /// Emulators on shared CI runners regularly show "X isn't responding" (launcher, Google apps)
    /// on top of the app, hiding it from UiAutomator. Choosing "Wait" gets it out of the way.
    /// </summary>
    public static void DismissSystemDialogs()
    {
        try
        {
            var wait = D.FindElements(MobileBy.AndroidUIAutomator(
                "new UiSelector().resourceId(\"android:id/aerr_wait\")")).FirstOrDefault();
            wait?.Click();
        }
        catch (WebDriverException)
        {
            // best effort
        }
    }
}

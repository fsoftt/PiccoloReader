namespace PiccoloReader;

public static class PlatformInfo
{
    public static bool IsAndroid => DeviceInfo.Platform == DevicePlatform.Android;
}

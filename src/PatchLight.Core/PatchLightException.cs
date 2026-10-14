namespace PatchLight.Core;

// Message is shown to the user as-is, so keep it plain English.
public class PatchLightException(string message) : Exception(message);

public sealed class ReleaseHealthFormatException(string message) : PatchLightException(message);

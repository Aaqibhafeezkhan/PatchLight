namespace PatchLight.Core;

// Pausing security updates is risky, so holds always end.
public static class HoldPolicy
{
    public const int DefaultDays = 14;
    public const int MaxDays = 35;
}

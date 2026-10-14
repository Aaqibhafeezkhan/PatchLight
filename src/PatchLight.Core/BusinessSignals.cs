using System.Text.RegularExpressions;

namespace PatchLight.Core;

// Spots known issues that almost only hit business PCs (domains, virtual desktops, servers, IT-managed
// devices). Only strong, specific wording counts, and only from the title, summary and description:
// workaround text mentions Group Policy and IT administrators for nearly every issue.
// A wrong "yes" would set a real home problem aside, so when in doubt the answer is no.
public static partial class BusinessSignals
{
    [GeneratedRegex(
        @"domain[- ]joined|joined to (a|an|the) (on-premises )?domain|Active Directory|domain controllers?|" +
        @"Azure Virtual Desktop|\bAVD\b|Windows 365|Cloud PC|virtual desktop environments?|" +
        @"Remote Desktop Services|\bRDS\b|Windows Server\b|\bWSUS\b|Configuration Manager|\bIntune\b|" +
        @"Administrator protection|enterprise[- ]managed|managed devices|" +
        @"Home users[^.]{0,80}unlikely|unlikely[^.]{0,80}Home (edition|users|devices)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Signal();

    public static bool IsLikelyBusinessOnly(string title, string summary, string description) =>
        Signal().IsMatch(title) || Signal().IsMatch(summary) || Signal().IsMatch(description);
}

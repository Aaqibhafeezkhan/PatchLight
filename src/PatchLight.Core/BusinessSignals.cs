using System.Text.RegularExpressions;

namespace PatchLight.Core;

// Spots known issues that only hit business PCs (domains, virtual desktops, servers, IT-managed devices).
// A wrong "yes" would set a real home problem aside, so the rules are strict and when in doubt the answer is no:
// - Words that only make sense for company PCs count anywhere in the title, summary or description.
// - Words that also turn up in passing (virtual desktops, Windows Server, Windows 365) count only in the title.
// - Sentences that rule something out ("does not affect", "any version of") are skipped.
// - Hedged wording ("primarily", "mostly", "more likely") means home PCs can get it too, so it counts.
// Workaround text is never read: it mentions Group Policy and IT administrators for nearly every issue.
public static partial class BusinessSignals
{
    [GeneratedRegex(
        @"domain[- ]joined|joined to (a|an|the) (on-premises )?domain|Active Directory|domain controllers?|" +
        @"Remote Desktop Services|\bWSUS\b|Configuration Manager|\bIntune\b|Administrator protection|" +
        @"enterprise[- ]managed|managed devices|" +
        @"Home users[^.]{0,80}unlikely|unlikely[^.]{0,80}Home (edition|users|devices)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Anywhere();

    [GeneratedRegex(@"Azure Virtual Desktop|\bAVD\b|Windows 365|Cloud PC|virtual desktop|Windows Server\b", RegexOptions.IgnoreCase)]
    private static partial Regex TitleOnly();

    [GeneratedRegex(@"\bprimarily\b|\bmostly\b|\bmainly\b|more likely|\bespecially\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hedge();

    [GeneratedRegex(@"does not affect|doesn't affect|not affected|any version of", RegexOptions.IgnoreCase)]
    private static partial Regex RulesOut();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    public static bool IsLikelyBusinessOnly(string title, string summary, string description)
    {
        if (Anywhere().IsMatch(title) || TitleOnly().IsMatch(title)) return true;

        var sentences = SentenceBreak().Split($"{summary} {description}").Where(s => !RulesOut().IsMatch(s)).ToList();
        if (sentences.Any(s => Hedge().IsMatch(s))) return false;
        return sentences.Any(s => Anywhere().IsMatch(s));
    }
}

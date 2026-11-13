using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nudge.Core;

namespace Nudge.Web.Data;

/// <summary>
/// Stores a recurrence rule as its canonical RRULE text, for example
/// <c>FREQ=WEEKLY;INTERVAL=2;BYDAY=FR</c>.
/// </summary>
internal sealed class RecurrenceRuleConverter()
    : ValueConverter<RecurrenceRule, string>(
        rule => rule.ToString(),
        text => RecurrenceRule.Parse(text));

/// <summary>
/// Two rules are the same if their text is the same. Without this,
/// replacing a rule with an identical one would count as a change
/// and be written back.
/// </summary>
internal sealed class RecurrenceRuleComparer()
    : ValueComparer<RecurrenceRule>(
        (left, right) => SameText(left, right),
        rule => rule.ToString().GetHashCode(),
        rule => rule)
{
    private static bool SameText(
        RecurrenceRule? left,
        RecurrenceRule? right)
    {
        return left is null || right is null
            ? ReferenceEquals(left, right)
            : left.ToString() == right.ToString();
    }
}

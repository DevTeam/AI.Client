namespace AI.Web.Settings;

using AI.Contracts.Settings;

public static class CommandRuleList
{
    /// <summary>
    /// The rules with <paramref name="rule"/> in place of one of the same tool, scope, owner and pattern,
    /// which is what the Host does when it saves one.
    /// </summary>
    public static IReadOnlyList<CommandRule> Replace(IReadOnlyList<CommandRule> rules, CommandRule rule) =>
        rules.Where(item => !(item.Tool == rule.Tool && item.Scope == rule.Scope && item.ProjectId == rule.ProjectId
                && item.ChatId == rule.ChatId && item.Pattern == rule.Pattern))
            .Append(rule).ToArray();
}

namespace AI.Web.Skills;

/// <summary>
/// Matches imported skills to the ones in the scope being edited by id, which names the skill's
/// file, and works out what each choice would do.
/// </summary>
public interface ISkillImportPlanner
{
    /// <param name="current">The skills of the scope, as they would be exported now.</param>
    /// <param name="builtInIds">Ids the application's own skills hold; no other skill may use them.</param>
    SkillImportPlan Plan(SkillTransferParseResult parsed, IReadOnlyList<SkillTransferItem> current, IReadOnlyCollection<string> builtInIds);

    /// <summary><paramref name="id"/>, or "id-2", "id-3" … when it is taken.</summary>
    string UniqueId(string id, IEnumerable<string> taken);
}

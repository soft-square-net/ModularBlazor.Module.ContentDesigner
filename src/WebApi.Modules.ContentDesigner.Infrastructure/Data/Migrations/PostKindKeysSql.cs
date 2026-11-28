using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Migrations;

/// <summary>
/// The data step of the <c>PostKindsAsFieldKeys</c> migration, the same on every engine (only the table names are quoted
/// differently): the <c>Kind</c> column of the document templates and of the post designs goes from the old enum names to
/// DynamicFields root field keys (<see cref="PostKindKeys.Legacy"/>). Nothing else stores a post kind: the views' JSON
/// names parts and tokens only. Down maps the six keys back and leaves every other key as it is.
/// </summary>
internal static class PostKindKeysSql
{
    private static readonly string[] Tables = ["document_templates", "post_designs"];

    /// <param name="table">Quotes a table of the module with its schema or prefix.</param>
    /// <param name="column">Quotes a column name.</param>
    /// <param name="national">Writes the literals as national strings (<c>N'...'</c>): Oracle refuses to compare its NVARCHAR2
    /// columns with plain literals in a CASE (ORA-12704).</param>
    public static IEnumerable<string> Up(Func<string, string> table, Func<string, string> column, bool national = false) =>
        Statements(table, column, national, [.. PostKindKeys.Legacy.Select(k => (From: k.Name, To: k.Key))]);

    public static IEnumerable<string> Down(Func<string, string> table, Func<string, string> column, bool national = false) =>
        Statements(table, column, national, [.. PostKindKeys.Legacy.Select(k => (From: k.Key, To: k.Name))]);

    private static IEnumerable<string> Statements(Func<string, string> table, Func<string, string> column, bool national,
        IReadOnlyList<(string From, string To)> map)
    {
        string L(string text) => (national ? "N'" : "'") + text + "'";
        var kind = column("Kind");
        var cases = string.Join(" ", map.Select(m => $"WHEN {L(m.From)} THEN {L(m.To)}"));
        var from = string.Join(", ", map.Select(m => L(m.From)));
        return Tables.Select(t => $"UPDATE {table(t)} SET {kind} = CASE {kind} {cases} END WHERE {kind} IN ({from})");
    }
}

namespace MsrEdu.Api.Data;

/// <summary>Raw SQL used by migrations, embedded from Data/Sql so it stays readable in pgAdmin and code review.</summary>
public static class SqlScripts
{
    public static string Read(string name)
    {
        using var stream = typeof(SqlScripts).Assembly.GetManifestResourceStream($"MsrEdu.Api.Data.Sql.{name}")
            ?? throw new InvalidOperationException($"Embedded SQL script {name} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

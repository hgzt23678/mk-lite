using System.Text.RegularExpressions;

namespace ActivityPub.Persistence.Tests;

public sealed partial class SqlInjectionGuardTests
{
    [Fact]
    public void ProductionSourceDoesNotUseRawOrDynamicallyComposedSql()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] sourceFiles = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src"),
            "*.cs",
            SearchOption.AllDirectories);
        string source = string.Join(
            "\n",
            sourceFiles.Select(path => File.ReadAllText(path)));

        string[] forbiddenApis =
        [
            ".FromSqlRaw(",
            ".ExecuteSqlRaw(",
            ".ExecuteSqlRawAsync(",
            ".SqlQueryRaw(",
            "new NpgsqlCommand("
        ];
        foreach (string forbiddenApi in forbiddenApis)
        {
            Assert.DoesNotContain(forbiddenApi, source, StringComparison.Ordinal);
        }

        foreach (Match assignment in CommandTextAssignment().Matches(source))
        {
            string expression = assignment.Groups["expression"].Value.Trim();
            Assert.StartsWith("\"", expression, StringComparison.Ordinal);
            Assert.EndsWith("\"", expression, StringComparison.Ordinal);
            Assert.DoesNotContain('$', expression);
            Assert.DoesNotContain('+', expression);
        }

        foreach (Match invocation in FromSqlInvocation().Matches(source))
        {
            string argument = invocation.Groups["argument"].Value.TrimStart();
            Assert.True(
                argument.StartsWith("$\"", StringComparison.Ordinal) ||
                argument.StartsWith('"'),
                $"FromSql must receive a literal or FormattableString interpolation, not a dynamically composed string: {argument}");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ActivityPubServer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found from the test output directory.");
    }

    [GeneratedRegex(@"\.CommandText\s*=\s*(?<expression>[^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex CommandTextAssignment();

    [GeneratedRegex(@"\.FromSql\(\s*(?<argument>[^\r\n]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FromSqlInvocation();
}

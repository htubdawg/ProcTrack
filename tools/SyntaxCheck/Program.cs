using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Usage: dotnet run --project tools/SyntaxCheck -- <src folder>
// Exit code 1 if any file has a syntax error.
internal static class Program
{
	private static int Main(string[] args)
	{
		string src = args.Length > 0 ? args[0] : "ProcTrack/src";
		CSharpParseOptions options = new CSharpParseOptions(LanguageVersion.CSharp10);
		int errors = 0;
		string[] files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
		foreach (string file in files.OrderBy(f => f))
		{
			SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
			foreach (Diagnostic d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
			{
				Console.WriteLine(d.ToString());
				errors++;
			}
		}
		Console.WriteLine(errors == 0 ? $"{files.Length} files parse cleanly." : $"{errors} syntax error(s).");
		return errors == 0 ? 0 : 1;
	}
}

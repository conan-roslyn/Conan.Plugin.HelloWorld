using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Conan.Plugin.HelloWorld
{
    /// <summary>
    /// The HelloWorld Conan plugin will add a
    /// `System.Console.WriteLine("Hello World from Conan!");` at the begining on the main entry point of your assembly.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HelloWorldCompilationRewriter : CompilationRewriter
    {
        private static readonly DiagnosticDescriptor ThisDescriptor = new DiagnosticDescriptor("HW0001", "HelloWorld", "{0}", "Design", DiagnosticSeverity.Warning, true);

        public override Compilation Rewrite(CompilationRewriterContext context)
        {
            var compilation = context.Compilation;

            // Look for Emtry{pomt
            var tokenSrc = new CancellationTokenSource();
            var symbol = compilation.GetEntryPoint(tokenSrc.Token);

            if (symbol == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(ThisDescriptor, Location.None, "Main entry point not found"));
                return compilation;
            }

            // Fetch the method
            var syntaxReference = symbol.DeclaringSyntaxReferences.First();

            var mainTree = syntaxReference.SyntaxTree;
            var root = mainTree.GetRoot();
            var originalMethod = (MethodDeclarationSyntax) syntaxReference.GetSyntax();
            var newMethod = originalMethod;

            SyntaxNode firstLocationNode = null;

            // Transform ExpressionBody into simple Body
            if (originalMethod.Body == null && originalMethod.ExpressionBody != null)
            {
                // Remove trailing ; of the expression body method
                var newSemiColonToken = Token(originalMethod.SemicolonToken.LeadingTrivia, SyntaxKind.None, originalMethod.SemicolonToken.TrailingTrivia);

                // Transform Expression body into a simple body
                firstLocationNode = originalMethod.ExpressionBody.Expression;
                newMethod = originalMethod.WithSemicolonToken(newSemiColonToken).WithExpressionBody(null).WithBody(Block(SingletonList<StatementSyntax>(ExpressionStatement(originalMethod.ExpressionBody.Expression))));
            }
            else if (originalMethod.Body != null)
            {
                firstLocationNode = originalMethod.Body.Statements.FirstOrDefault();
            }

            if (newMethod.Body == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(ThisDescriptor, Location.None, "Main entry point empty"));
                return compilation;
            }

            // Calculate the line of the first statement
            int nextLine = 0;
            if (firstLocationNode != null)
            {
                nextLine = firstLocationNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
            else
            {
                // Otherwise take whatever trailing trivia the body has
                var trailingTrivia = newMethod.Body.GetTrailingTrivia();
                if (trailingTrivia.Count > 0)
                {
                    nextLine = trailingTrivia[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                }
                else
                {
                    // TODO: No line found?
                }
            }

            // Create the following code (done with https://roslynquoter.azurewebsites.net)
            //#line hidden
            //System.Console.WriteLine("Hello World from Conan!");
            //#line 9
            // TODO: Check if the following code is the most optimal way to manipulate efficiently the Compilation/Syntaxtrees

            var helloWorldFromConan = ExpressionStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(Identifier("System")),
                                    IdentifierName("Console")),
                                IdentifierName("WriteLine")))
                        .WithArgumentList(
                            ArgumentList(
                                SingletonSeparatedList(
                                    Argument(
                                        LiteralExpression(
                                            SyntaxKind.StringLiteralExpression,
                                            Literal("Hello World from Conan!")))))))
                // #line number
                .WithTrailingTrivia(TriviaList(Trivia(LineDirectiveTrivia(Literal(nextLine), true))))
                .NormalizeWhitespace();

            // #line default
            var leadingTrivias = newMethod.GetLeadingTrivia();
            leadingTrivias = leadingTrivias.Insert(0, LineDirectiveHidden());
            leadingTrivias = leadingTrivias.Insert(0, CarriageReturnLineFeed);
            newMethod = newMethod.WithLeadingTrivia(leadingTrivias);

            var statements = newMethod.Body.Statements;
            statements = statements.Insert(0, helloWorldFromConan);
            newMethod = newMethod.WithBody(newMethod.Body.WithStatements(statements));

            var newRoot = root.ReplaceNode(originalMethod, newMethod);

            // Replace the syntax tree
            compilation = compilation.ReplaceSyntaxTree(mainTree, newRoot.SyntaxTree);

            // Just store the file on the disk (but it is not used by the debugger, as we are directly working on the original file)
            var newMainFile = context.GetOutputFilePath("NewMain");
            System.IO.File.WriteAllText(newMainFile, newRoot.ToFullString());

            context.ReportDiagnostic(Diagnostic.Create(ThisDescriptor, Location.None, $"Main method successfuly modified by the HelloWorld Conan Plugin (See modified file at: {newMainFile})"));

            return compilation;
        }

        private static SyntaxTrivia LineDirectiveHidden()
        {
            // Emit the trivia: #line hidden
            // with proper spaces and \r\n
            return Trivia(
                LineDirectiveTrivia(
                        Token(SyntaxKind.HiddenKeyword),
                        true)
                    .WithLineKeyword(
                        Token(
                            TriviaList(),
                            SyntaxKind.LineKeyword,
                            TriviaList(
                                Space)))
                    .WithEndOfDirectiveToken(
                        Token(
                            TriviaList(),
                            SyntaxKind.EndOfDirectiveToken,
                            TriviaList(
                                CarriageReturnLineFeed))));
        }
    }
}

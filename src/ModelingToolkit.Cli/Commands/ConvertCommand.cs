using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary><c>mtk convert input output</c>: reads a model and writes it in the format of the output extension.</summary>
internal static class ConvertCommand
{
    public static Command Create(IServiceProvider services)
    {
        Argument<FileInfo> input = new Argument<FileInfo>("input") { Description = "The model to convert." }
            .AcceptExistingOnly();
        var output = new Argument<FileInfo>("output")
        {
            Description = "The file to write; its extension chooses the format.",
        };

        var command = new Command("convert", "Convert a model to another format.") { input, output };
        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo inputFile = parseResult.GetRequiredValue(input);
            FileInfo outputFile = parseResult.GetRequiredValue(output);
            var formats = services.GetRequiredService<DocumentFormats>();

            // Both formats are checked before the work starts.
            IDocumentReader reader = formats.ReaderFor(inputFile.FullName);
            IDocumentWriter writer = formats.WriterFor(outputFile.FullName);

            Document document = reader.Read(inputFile.FullName);
            writer.Write(document, outputFile.FullName);

            console.WriteLine(
                $"Converted {inputFile.Name} ({reader.FormatName}) to {outputFile.Name} ({writer.FormatName}).");
            return 0;
        }));
        return command;
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLamaSharp.KernelMemory;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;

namespace LLama.Examples.Examples
{
    public class KernelMemory
    {
        public static async Task Run(ChatSession session)
        {
            Console.WriteLine("Example from: https://github.com/microsoft/kernel-memory/blob/main/examples/101-using-core-nuget/Program.cs");
            Console.Write("Please input your model path: ");
            var modelPath = Console.ReadLine();
            var memory = new KernelMemoryBuilder()
                    .WithChatSessionDefaults(new ChatSessionConfig(modelPath)
                    {
                        DefaultInferenceParams = new Common.InferenceParams
                        {
                            AntiPrompts = new List<string> { "\n\n" }
                        }
                    })
                    .With(new TextPartitioningOptions
                    {
                        MaxTokensPerParagraph = 300,
                        MaxTokensPerLine = 100,
                        OverlappingTokens = 30
                    })
                .Build();

            session.AddDocument(@"./Assets/sample-SK-Readme.pdf");

            var question = "What's Semantic Kernel?";

            Console.WriteLine($"\n\nQuestion: {question}");

            var answer = await memory.AskAsync(question);

            Console.WriteLine($"\nAnswer: {answer.Result}");

            Console.WriteLine("\n\n  Sources:\n");

            foreach (var x in answer.RelevantSources)
            {
                Console.WriteLine($"  - {x.SourceName}  - {x.Link} [{x.Partitions.First().LastUpdate:D}]");
            }
        }
    }
}

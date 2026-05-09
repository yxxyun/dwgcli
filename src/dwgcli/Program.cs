// Copyright 2026 dwgcli
// SPDX-License-Identifier: Apache-2.0

Console.OutputEncoding = System.Text.Encoding.UTF8;

// `dwgcli help` dispatches to root --help
if (args.Length > 0 && args[0] is "--help" or "-h" or "-?")
{
    args = args.Skip(1).Any()
        ? new[] { "help" }.Concat(args.Skip(1)).ToArray()
        : new[] { "help" };
}

var rootCommand = DwgCli.CommandBuilder.BuildRootCommand();

if (args.Length == 0)
{
    rootCommand.Parse("-h").Invoke();
    return 0;
}

return rootCommand.Parse(args).Invoke();

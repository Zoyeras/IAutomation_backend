using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Tools;

internal static class Program
{
    private static string Strip(string s) => Regex.Replace(s ?? string.Empty, "<.*?>", string.Empty).Trim();

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Uso: <path.html> [nit] [empresa]");
            return 2;
        }

        var path = args[0];
        var nit = args.Length >= 2 ? args[1].Trim() : string.Empty;
        var empresa = args.Length >= 3 ? args[2].Trim() : string.Empty;

        if (!File.Exists(path))
        {
            Console.WriteLine($"No existe el archivo: {path}");
            return 3;
        }

        var html = File.ReadAllText(path);

        var tbodyMatch = Regex.Match(html, "<tbody>(?<body>[\\s\\S]*?)</tbody>", RegexOptions.IgnoreCase);
        if (!tbodyMatch.Success)
        {
            Console.WriteLine("No se encontró <tbody>");
            return 4;
        }

        var body = tbodyMatch.Groups["body"].Value;
        var rowMatches = Regex.Matches(body, "<tr>(?<row>[\\s\\S]*?)</tr>", RegexOptions.IgnoreCase);
        Console.WriteLine($"Filas detectadas: {rowMatches.Count}");

        for (var i = 0; i < rowMatches.Count; i++)
        {
            var rowHtml = rowMatches[i].Groups["row"].Value;
            var cells = Regex.Matches(rowHtml, "<td>(?<td>[\\s\\S]*?)</td>", RegexOptions.IgnoreCase);
            if (cells.Count < 5) continue;

            var ticket = Strip(cells[0].Groups["td"].Value);
            var nitRow = Strip(cells[3].Groups["td"].Value);
            var empresaRow = Strip(cells[4].Groups["td"].Value);

            Console.WriteLine($"[{i}] Ticket={ticket} Nit={nitRow} Empresa={empresaRow}");

            if (!string.IsNullOrWhiteSpace(nit) && string.Equals(nitRow, nit, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"MATCH por NIT => {ticket}");
                return 0;
            }
        }

        Console.WriteLine("No hubo MATCH por NIT (offline)");
        if (!string.IsNullOrWhiteSpace(empresa))
            Console.WriteLine($"Empresa esperada: {empresa}");

        return 1;
    }
}

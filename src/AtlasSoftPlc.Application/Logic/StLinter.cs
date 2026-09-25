using System.Text.RegularExpressions;
using AtlasSoftPlc.Domain.Ir;
using System.Collections.Generic;
using System.Linq;

namespace AtlasSoftPlc.Application.Logic;

/// <summary>
/// Validador de sintaxis para código Structured Text (ST) según el estándar IEC 61131-3.
/// Actúa como un Linter para asegurar que el código generado sea aceptable por el compilador del PLC.
/// </summary>
public class StLinter
{
    public sealed class LinterResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public LinterResult Lint(string stCode)
    {
        var result = new LinterResult { IsValid = true };
        var lines = stCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // 1. Verificar bloques obligatorios
        if (!stCode.Contains("VAR") || !stCode.Contains("END_VAR"))
        {
            result.Errors.Add("Faltan los bloques de declaración de variables (VAR/END_VAR).");
            result.IsValid = false;
        }

        // 2. Análisis línea por línea
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            // Verificar terminación de sentencias con punto y coma (excepto en VAR/END_VAR)
            if (!line.StartsWith("VAR") && !line.StartsWith("END_VAR") && !line.EndsWith(";"))
            {
                result.Errors.Add($"Línea {i + 1}: Falta punto y coma (;) al final de la sentencia.");
                result.IsValid = false;
            }

            // Verificar paréntesis balanceados
            if (CountOccurrences(line, '(') != CountOccurrences(line, ')'))
            {
                result.Errors.Add($"Línea {i + 1}: Paréntesis no balanceados.");
                result.IsValid = false;
            }
        }

        // 3. Verificación de palabras clave prohibidas o mal usadas
        // (Simulación de análisis sintáctico básico)
        if (stCode.Contains("=") && !stCode.Contains(":="))
        {
            // En ST, la asignación es := no =
            // Ignoramos si está dentro de una comparación (ej. A = B)
            // Para el MVP, marcamos advertencia si hay '=' solo
            if (Regex.IsMatch(stCode, @"\w+\s+=\s+\w+"))
            {
                result.Warnings.Add("Se detectó el operador '='. Recuerde que para asignaciones en ST se debe usar ':='.");
            }
        }

        return result;
    }

    private int CountOccurrences(string text, char c) => text.Count(x => x == c);
}

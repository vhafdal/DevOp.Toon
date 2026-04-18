using System.Globalization;

namespace DevOp.Toon.SpecGenerator.Extensions;

public static class StringExtensions
{
    public static string ToPascalCase(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        string formattedInput = input.Replace("-", " ");

        TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
        string titleCase = textInfo.ToTitleCase(formattedInput.ToLower());
        return titleCase.Replace(" ", "");
    }
}

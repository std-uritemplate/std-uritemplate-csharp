#nullable disable
namespace Std;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class UriTemplate
{
    // Public API
    public static string Expand(string template, IReadOnlyDictionary<string, object> substitutions)
    {
        return ExpandImpl(template, substitutions);
    }

    // Private implementation
    private enum Operator
    {
        NO_OP,
        PLUS,
        HASH,
        DOT,
        SLASH,
        SEMICOLON,
        QUESTION_MARK,
        AMP
    }

    private static void CheckVarname(string token, int col)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
        if (token.EndsWith('.'))
#else
        if (token.EndsWith("."))
#endif
        {
            throw new ArgumentException($"Variable name cannot end with '.' at col:{col}");
        }
        if (token.Contains(".."))
        {
            throw new ArgumentException($"Variable name cannot contain '..' at col:{col}");
        }
        for (int i = 0; i < token.Length; i++)
        {
            if (token[i] == '%')
            {
                if (i + 2 >= token.Length
                    || !IsHexDigit(token[i + 1])
                    || !IsHexDigit(token[i + 2]))
                {
                    throw new ArgumentException($"Invalid percent encoding in variable name at col:{col}");
                }
            }
        }
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
    }

    private static void ValidateLiteral(char c, int col)
    {
        switch (c)
        {
            case '+':
            case '#':
            case '/':
            case ';':
            case '?':
            case '&':
            case ' ':
            case '!':
            case '=':
            case '$':
            case '|':
            case '*':
            case ':':
            case '~':
            case '-':
                throw new ArgumentException($"Illegal character identified in the token at col:{col}");
            default:
                break;
        }
    }

    private static int GetMaxChar(StringBuilder buffer, int col)
    {
        if (buffer == null || buffer.Length < 1)
        {
            return -1;
        }

        string str = buffer.ToString();
        int result;
        try
        {
            result = int.Parse(str);
        }
        catch (FormatException)
        {
            throw new ArgumentException($"Cannot parse max chars at col:{col}");
        }

        if (str[0] == '0')
        {
            throw new ArgumentException($"Leading zeros are not allowed in max chars at col:{col}");
        }
        if (result < 1 || result > 9999)
        {
            throw new ArgumentException($"Max chars must be between 1 and 9999 at col:{col}");
        }

        return result;
    }

    private static Operator GetOperator(char c, StringBuilder token, int col)
    {
        switch (c)
        {
            case '+': return Operator.PLUS;
            case '#': return Operator.HASH;
            case '.': return Operator.DOT;
            case '/': return Operator.SLASH;
            case ';': return Operator.SEMICOLON;
            case '?': return Operator.QUESTION_MARK;
            case '&': return Operator.AMP;
            default:
                ValidateLiteral(c, col);
                token.Append(c);
                return Operator.NO_OP;
        }
    }

    private static string ExpandImpl(string str, IReadOnlyDictionary<string, object> substitutions)
    {
        StringBuilder result = new StringBuilder(str.Length * 2);

        bool toToken = false;
        StringBuilder token = new StringBuilder();

        Operator? op = null;
        bool composite = false;
        bool toMaxCharBuffer = false;
        StringBuilder maxCharBuffer = new StringBuilder(3);
        bool firstToken = true;

        for (int i = 0; i < str.Length; i++)
        {
            char character = str[i];
            switch (character)
            {
                case '{':
                    toToken = true;
                    token.Clear();
                    firstToken = true;
                    break;
                case '}':
                    if (toToken)
                    {
                        if (toMaxCharBuffer && maxCharBuffer.Length == 0)
                        {
                            throw new ArgumentException($"Empty prefix after colon at col:{i}");
                        }
                        bool expanded = ExpandToken(op, token.ToString(), composite, GetMaxChar(maxCharBuffer, i), firstToken, substitutions, result, i);
                        if (expanded && firstToken)
                        {
                            firstToken = false;
                        }
                        toToken = false;
                        token.Clear();
                        op = null;
                        composite = false;
                        toMaxCharBuffer = false;
                        maxCharBuffer.Clear();
                    }
                    else
                    {
                        throw new ArgumentException($"Failed to expand token, invalid at col:{i}");
                    }
                    break;
                case ',':
                    if (toToken)
                    {
                        if (toMaxCharBuffer && maxCharBuffer.Length == 0)
                        {
                            throw new ArgumentException($"Empty prefix after colon at col:{i}");
                        }
                        bool expanded = ExpandToken(op, token.ToString(), composite, GetMaxChar(maxCharBuffer, i), firstToken, substitutions, result, i);
                        if (expanded && firstToken)
                        {
                            firstToken = false;
                        }
                        token.Clear();
                        composite = false;
                        toMaxCharBuffer = false;
                        maxCharBuffer.Clear();
                        break;
                    }
                    // Intentional fall-through for commas outside the {}
                    goto default;
                default:
                    if (toToken)
                    {
                        if (op == null)
                        {
                            op = GetOperator(character, token, i);
                        }
                        else if (toMaxCharBuffer)
                        {
                            if (char.IsDigit(character))
                            {
                                maxCharBuffer.Append(character);
                            }
                            else
                            {
                                throw new ArgumentException($"Illegal character identified in the token at col:{i}");
                            }
                        }
                        else
                        {
                            if (character == ':')
                            {
                                toMaxCharBuffer = true;
                                maxCharBuffer.Clear();
                            }
                            else if (character == '*')
                            {
                                composite = true;
                            }
                            else
                            {
                                ValidateLiteral(character, i);
                                token.Append(character);
                            }
                        }
                    }
                    else
                    {
                        if (character > 0x7F || char.IsHighSurrogate(character))
                        {
                            string toEncode;
                            if (char.IsHighSurrogate(character) && i + 1 < str.Length && char.IsLowSurrogate(str[i + 1]))
                            {
                                toEncode = str.Substring(i, 2);
                                i++;
                            }
                            else
                            {
                                toEncode = character.ToString();
                            }
                            byte[] bytes = Encoding.UTF8.GetBytes(toEncode);
                            foreach (byte b in bytes)
                            {
                                result.Append($"%{b:X2}");
                            }
                        }
                        else
                        {
                            result.Append(character);
                        }
                    }
                    break;
            }
        }

        if (!toToken)
        {
            return result.ToString();
        }
        else
        {
            throw new ArgumentException("Unterminated token");
        }
    }

    private static void AddPrefix(Operator? op, StringBuilder result)
    {
        switch (op)
        {
            case Operator.HASH:
                result.Append('#');
                break;
            case Operator.DOT:
                result.Append('.');
                break;
            case Operator.SLASH:
                result.Append('/');
                break;
            case Operator.SEMICOLON:
                result.Append(';');
                break;
            case Operator.QUESTION_MARK:
                result.Append('?');
                break;
            case Operator.AMP:
                result.Append('&');
                break;
            default:
                return;
        }
    }

    private static void AddSeparator(Operator? op, StringBuilder result)
    {
        switch (op)
        {
            case Operator.DOT:
                result.Append('.');
                break;
            case Operator.SLASH:
                result.Append('/');
                break;
            case Operator.SEMICOLON:
                result.Append(';');
                break;
            case Operator.QUESTION_MARK:
            case Operator.AMP:
                result.Append('&');
                break;
            default:
                result.Append(',');
                break;
        }
    }

    private static void AddValue(Operator? op, string token, object value, StringBuilder result, int maxChar)
    {
        switch (op)
        {
            case Operator.PLUS:
            case Operator.HASH:
                AddExpandedValue(null, value, result, maxChar, false);
                break;
            case Operator.QUESTION_MARK:
            case Operator.AMP:
                result.Append(token + '=');
                AddExpandedValue(null, value, result, maxChar, true);
                break;
            case Operator.SEMICOLON:
                result.Append(token);
                AddExpandedValue("=", value, result, maxChar, true);
                break;
            case Operator.DOT:
            case Operator.SLASH:
            case Operator.NO_OP:
                AddExpandedValue(null, value, result, maxChar, true);
                break;
        }
    }

    private static void AddValueElement(Operator? op, string token, object value, StringBuilder result, int maxChar)
    {
        switch (op)
        {
            case Operator.PLUS:
            case Operator.HASH:
                AddExpandedValue(null, value, result, maxChar, false);
                break;
            case Operator.QUESTION_MARK:
            case Operator.AMP:
            case Operator.SEMICOLON:
            case Operator.DOT:
            case Operator.SLASH:
            case Operator.NO_OP:
                AddExpandedValue(null, value, result, maxChar, true);
                break;
        }
    }

    private static bool isSurrogate(char cp) {
        return (cp >= 0xD800 && cp <= 0xDFFF);
    }

    private static bool isIprivate(char cp) {
        return (0xE000 <= cp && cp <= 0xF8FF);
    }

    private static bool isUcschar(char cp) {
        return (0xA0 <= cp && cp <= 0xD7FF)
                || (0xF900 <= cp && cp <= 0xFDCF)
                || (0xFDF0 <= cp && cp <= 0xFFEF);
    }

    private static void AddExpandedValue(string prefix, object value, StringBuilder result, int maxChar, bool replaceReserved)
    {
        string stringValue = convertNativeTypes(value);
        int codePointCount = 0;
        for (int ci = 0; ci < stringValue.Length; ci++)
        {
            if (char.IsHighSurrogate(stringValue[ci]) && ci + 1 < stringValue.Length && char.IsLowSurrogate(stringValue[ci + 1]))
            {
                ci++;
            }
            codePointCount++;
        }
        int max = (maxChar != -1) ? Math.Min(maxChar, codePointCount) : codePointCount;
        result.EnsureCapacity(max * 2); // hint to SB
        bool toReserved = false;
        StringBuilder reservedBuffer = new StringBuilder(3);

        if (max > 0 && prefix != null)
        {
            result.Append(prefix);
        }

        int charCount = 0;
        for (int i = 0; i < stringValue.Length && charCount < max; i++)
        {
            char character = stringValue[i];

            if (character == '%' && !replaceReserved)
            {
                toReserved = true;
                reservedBuffer.Clear();
            }

            var toAppend = character.ToString();
            if (isSurrogate(character)) {
                toAppend = Uri.EscapeDataString(char.ConvertFromUtf32(char.ConvertToUtf32(stringValue, i)));
                i++; // skip the low surrogate
            } else if (replaceReserved || isUcschar(character) || isIprivate(character)) {
                toAppend = Uri.EscapeDataString(toAppend);
            }

            if (toReserved)
            {
                reservedBuffer.Append(toAppend);

                if (reservedBuffer.Length == 3)
                {
                    bool isEncoded = false;
                    try
                    {
                        var original = reservedBuffer.ToString();
                        isEncoded = !original.Equals(Uri.UnescapeDataString(original));
                    }
                    catch (Exception)
                    {
                        // ignore
                    }

                    if (isEncoded)
                    {
                        result.Append(reservedBuffer);
                    }
                    else
                    {
                        result.Append("%25");
                        // only if !replaceReserved
                        result.Append(reservedBuffer.ToString(1, 2));
                    }
                    toReserved = false;
                    reservedBuffer.Clear();
                }
            }
            else
            {
                if (character == ' ')
                {
                    result.Append("%20");
                }
                else if (character == '%')
                {
                    result.Append("%25");
                }
                else
                {
                    result.Append(toAppend);
                }
            }

            charCount++;
        }

        if (toReserved)
        {
            result.Append("%25");
            result.Append(reservedBuffer.ToString(1, reservedBuffer.Length - 1));
        }
    }

    private static bool IsList(object value)
    {
        return value is IList;
    }

    private static bool IsDictionary(object value)
    {
        return value is IDictionary;
    }

    private enum SubstitutionType
    {
        EMPTY,
        STRING,
        LIST,
        DICTIONARY
    }

    private static SubstitutionType GetSubstitutionType(object value, int col)
    {
        if (value == null)
        {
            return SubstitutionType.EMPTY;
        }
        if (isNativeType(value))
        {
            return SubstitutionType.STRING;
        }
        else if (IsList(value))
        {
            return SubstitutionType.LIST;
        }
        else if (IsDictionary(value))
        {
            return SubstitutionType.DICTIONARY;
        }
        else
        {
            throw new ArgumentException($"Illegal class passed as substitution, found {value.GetType()} at col:{col}");
        }
    }

    private static bool IsEmpty(SubstitutionType substType, object value)
    {
        if (value == null)
        {
            return true;
        }
        else
        {
            switch (substType)
            {
                case SubstitutionType.STRING:
                    return value == null;
                case SubstitutionType.LIST:
                    return ((IList)value).Count == 0;
                case SubstitutionType.DICTIONARY:
                    return ((IDictionary)value).Count == 0;
                default:
                    return true;
            }
        }
    }

    private static bool isNativeType(object value)
    {
        return value is string or bool or int or long or float or double or decimal;
    }

    private static string convertNativeTypes(object value)
    {
        return value switch
        {
            string str => str,
            bool => value.ToString().ToLower(),
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            float number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"Illegal class passed as substitution, found {value.GetType()}"),
        };
    }

    // returns true if expansion happened
    private static bool ExpandToken(
            Operator? op,
            string token,
            bool composite,
            int maxChar,
            bool firstToken,
            IReadOnlyDictionary<string, object> substitutions,
            StringBuilder result,
            int col)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException($"Found an empty token at col:{col}");
        }

        CheckVarname(token, col);

        object value;
        substitutions.TryGetValue(token, out value);
        SubstitutionType substType = GetSubstitutionType(value, col);
        if (substType == SubstitutionType.EMPTY || IsEmpty(substType, value))
        {
            return false;
        }

        if (firstToken)
        {
            AddPrefix(op, result);
        }
        else
        {
            AddSeparator(op, result);
        }

        switch (substType)
        {
            case SubstitutionType.STRING:
                AddStringValue(op, token, value, result, maxChar);
                break;
            case SubstitutionType.LIST:
                AddListValue(op, token, (IList)value, result, maxChar, composite);
                break;
            case SubstitutionType.DICTIONARY:
                AddDictionaryValue(op, token, ((IDictionary)value), result, maxChar, composite);
                break;
        }

        return true;
    }

    private static bool AddStringValue(Operator? op, string token, object value, StringBuilder result, int maxChar)
    {
        AddValue(op, token, value, result, maxChar);
        return true;
    }

    private static bool AddListValue(Operator? op, string token, IList value, StringBuilder result, int maxChar, bool composite)
    {
        bool first = true;
        foreach (object v in value)
        {
            if (first)
            {
                AddValue(op, token, v, result, maxChar);
                first = false;
            }
            else
            {
                if (composite)
                {
                    AddSeparator(op, result);
                    AddValue(op, token, v, result, maxChar);
                }
                else
                {
                    result.Append(',');
                    AddValueElement(op, token, v, result, maxChar);
                }
            }
        }
        return !first;
    }

    private static bool AddDictionaryValue(Operator? op, string token, IDictionary value, StringBuilder result, int maxChar, bool composite)
    {
        bool first = true;
        if (maxChar != -1)
        {
            throw new ArgumentException("Value trimming is not allowed on Dictionaries");
        }
        foreach (DictionaryEntry v in value)
        {
            if (composite)
            {
                if (!first)
                {
                    AddSeparator(op, result);
                }
                AddValueElement(op, token, (string)v.Key, result, maxChar);
                result.Append('=');
            }
            else
            {
                if (first)
                {
                    AddValue(op, token, (string)v.Key, result, maxChar);
                }
                else
                {
                    result.Append(',');
                    AddValueElement(op, token, (string)v.Key, result, maxChar);
                }
                result.Append(',');
            }
            AddValueElement(op, token, v.Value, result, maxChar);
            first = false;
        }
        return !first;
    }
}

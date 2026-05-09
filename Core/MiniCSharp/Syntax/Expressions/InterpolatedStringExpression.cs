using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class InterpolatedStringTemplate
    {
        private readonly List<InterpolatedStringPart> _parts = new List<InterpolatedStringPart>();

        public IReadOnlyList<InterpolatedStringPart> Parts => _parts;

        public void AddText(string text)
        {
            _parts.Add(InterpolatedStringPart.Text(text));
        }

        public void AddExpression(string expressionSource)
        {
            _parts.Add(InterpolatedStringPart.Expression(expressionSource));
        }
    }

    internal readonly struct InterpolatedStringPart
    {
        private InterpolatedStringPart(string text, string expressionSource, bool isExpression)
        {
            TextValue = text;
            ExpressionSource = expressionSource;
            IsExpression = isExpression;
        }

        public string TextValue { get; }

        public string ExpressionSource { get; }

        public bool IsExpression { get; }

        public static InterpolatedStringPart Text(string text)
        {
            return new InterpolatedStringPart(text, null, false);
        }

        public static InterpolatedStringPart Expression(string expressionSource)
        {
            return new InterpolatedStringPart(null, expressionSource, true);
        }
    }

    internal sealed class InterpolatedStringExpression : Expression
    {
        private readonly List<InterpolatedPartExpression> _parts;

        public InterpolatedStringExpression(InterpolatedStringTemplate template, Func<string, Type> resolveType)
        {
            _parts = new List<InterpolatedPartExpression>(template.Parts.Count);

            for (int i = 0; i < template.Parts.Count; i++)
            {
                InterpolatedStringPart part = template.Parts[i];

                if (part.IsExpression)
                {
                    Expression expression = Parser.ParseInlineExpression(part.ExpressionSource, resolveType);
                    _parts.Add(InterpolatedPartExpression.Expression(expression));
                }
                else
                {
                    _parts.Add(InterpolatedPartExpression.Text(part.TextValue));
                }
            }
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < _parts.Count; i++)
            {
                InterpolatedPartExpression part = _parts[i];

                if (part.IsExpression)
                {
                    object value = part.ExpressionValue.Evaluate(context).Value;

                    if (value != null)
                    {
                        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    builder.Append(part.TextValue);
                }
            }

            return RuntimeValue.From(builder.ToString());
        }

        private readonly struct InterpolatedPartExpression
        {
            private InterpolatedPartExpression(string textValue, Expression expressionValue, bool isExpression)
            {
                TextValue = textValue;
                ExpressionValue = expressionValue;
                IsExpression = isExpression;
            }

            public string TextValue { get; }

            public Expression ExpressionValue { get; }

            public bool IsExpression { get; }

            public static InterpolatedPartExpression Text(string text)
            {
                return new InterpolatedPartExpression(text, null, false);
            }

            public static InterpolatedPartExpression Expression(Expression expression)
            {
                return new InterpolatedPartExpression(null, expression, true);
            }
        }
    }
}

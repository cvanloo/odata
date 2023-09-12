using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace TransferERP.QueryLang;

public sealed class QueryParser<T> : IQueryLang<T>
{
	private Dictionary<string, string>? _nameMappings;

	private static class Grammar
	{
		public const string Equal = "eq";
		public const string NotEqual = "ne";
		public const string Or = "or";
		public const string And = "and";
	}
	
	public Expression<Func<T, bool>> Parse(string query)
	{
		var lexer = new QueryLexer(query);
		var tokens = lexer.Lex();
		var postfixTokens = QueryLexer.ToPostfix(tokens);
		
		// ToPostfix returns tokens in reversed order, so that when creating
		// the stack the order is reversed again/fixed.
		var tokenStack = new Stack<Token>(postfixTokens);
		
		// Stores the (intermediate) calculation results.
		var calcStack = new Stack<Expression>();
		
		// Object to run the query on.
		// We will construct a predicate with it as parameter.
		var argParam = Expression.Parameter(typeof(T), "t");
		
		while (tokenStack.Count > 0)
		{
			var token = tokenStack.Pop();
			
			switch (token.Type)
			{
				case TokenType.Identifier:
				{
					var propertyName = RealName(token.Value);
					try
					{
						var property = Expression.Property(argParam, propertyName);
						calcStack.Push(property);
					}
					catch (ArgumentException ex)
					{
						throw new Exception($"no property named `{propertyName}` on `{argParam}`", ex);
					}
					break;
				}
				case TokenType.ConstantNum:
				{
					var num = int.Parse(token.Value);
					var numConstant = Expression.Constant(num);
					calcStack.Push(numConstant);
					break;
				}
				case TokenType.ConstantText:
				{
					var textConstant = Expression.Constant(token.Value);
					calcStack.Push(textConstant);
					break;
				}
				case TokenType.Expression:
				{
					var rhs = calcStack.Pop();
					var lhs = calcStack.Pop();
					var expr = token.Value switch
					{
						Grammar.Equal => Expression.Equal(lhs, rhs),
						Grammar.NotEqual => Expression.NotEqual(lhs, rhs),
						Grammar.Or => Expression.Or(lhs, rhs),
						Grammar.And => Expression.AndAlso(lhs, rhs),
						_ => throw new NotSupportedException("expression not recognized by parser")
					};
					calcStack.Push(expr);
					break;
				}
				default:
					throw new NotSupportedException("unknown or invalid lexer token");
			}
		}

		var resultQuery = calcStack.Pop();
		if (calcStack.Count > 0) throw new Exception("equation must evaluate to a single result");
		return Expression.Lambda<Func<T, bool>>(resultQuery, argParam);
	}
	
	public bool TryParse(string query, out Expression<Func<T, bool>> result)
	{
		try
		{
			result = Parse(query);
			return true;
		}
		catch (Exception)
		{
			result = t => false; // Zero value
			return false;
		}
	}

	private static Dictionary<string, string> JsonToRealNames()
	{
		var nameDict = new Dictionary<string, string>();
		
		var props = typeof(T).GetProperties();
		foreach (var prop in props)
		{
			var attrs = prop.GetCustomAttributes(true);
			foreach (var attr in attrs)
			{
				if (attr is JsonPropertyNameAttribute jsonAttr)
				{
					var propName = prop.Name;
					var jsonName = jsonAttr.Name;
					
					nameDict.Add(jsonName, propName);
				}
			}
		}

		return nameDict;
	}

	private string RealName(string jsonName)
	{
		_nameMappings ??= JsonToRealNames();

		var success = _nameMappings.TryGetValue(jsonName, out var realName);
		if (success && !string.IsNullOrEmpty(realName))
			return realName;
		return jsonName;
	}
}

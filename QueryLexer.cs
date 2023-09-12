namespace TransferERP.QueryLang;

public enum TokenType
{
	Identifier,
	Expression,
	ConstantNum,
	ConstantText,
	PrecedenceOpen,
	PrecedenceClose
}

public record struct Token(TokenType Type, string Value);

private delegate LexerFunc LexerFunc();

public sealed class QueryLexer
{
	private const string SPACE = " ";
	private const string OPEN = "(";
	private const string CLOSE = ")";
	
	private readonly string _input;
	
	// Lexer state is represented using a function that can lex a piece of the
	// source code, and then return another function to lex the next piece.
	private LexerFunc _state;
	
	private int _pos;
	
	private readonly List<Token> _tokens = new();
	
	public QueryLexer(string input)
	{
		_input = input;
		_state = LexIdentifierOrConstantOrOpen;
		_pos = 0;
	}

	private string InputToEnd => _input[_pos..];

	public IEnumerable<Token> Lex()
	{
		while (_pos < _input.Length)
			_state = _state();

		return _tokens;
	}

	public static IEnumerable<Token> ToPostfix(IEnumerable<Token> tokens)
	{
		var exprs = new Stack<Token>();
		var output = new Stack<Token>();

		foreach (var token in tokens)
		{
			switch (token.Type)
			{
				case TokenType.Expression:
				{
					if (exprs.Count == 0
					    || exprs.Peek().Type == TokenType.PrecedenceOpen
					    || IsHigherOrEqualPrecedence(token, exprs.Peek()))
					{
						exprs.Push(token);
					}
					else
					{
						// Pop all operators with precedence greater or equal to current token, or until precedence
						// modifier '(' or ')' is reached.
						while (exprs.TryPeek(out var op))
						{
							if (op.Type is TokenType.PrecedenceOpen or TokenType.PrecedenceClose)
								break;

							if (IsHigherOrEqualPrecedence(op, token))
								output.Push(exprs.Pop());
							else
								break;
						}
						exprs.Push(token);
					}
					continue;
				}
				case TokenType.PrecedenceOpen:
					exprs.Push(token);
					continue;
				case TokenType.PrecedenceClose:
				{
					while (exprs.TryPop(out var op) && op.Type != TokenType.PrecedenceOpen)
					{
						output.Push(op);
					}
					continue;
				}
				case TokenType.ConstantText:
				case TokenType.ConstantNum:
				case TokenType.Identifier:
					output.Push(token);
					break;
				default:
					throw new ArgumentException($"unknown token type: `{token.Type}`");
			}
		}
		
		while (exprs.Count > 0)
			output.Push(exprs.Pop());

		return output.ToList();
	}

	private static bool IsHigherOrEqualPrecedence(Token l, Token r)
	{
		if (l.Value != "and") return true;
		return l.Value == r.Value;
	}

	private void SkipWhitespace()
	{
		while (_input[_pos] == ' ')
			_pos++;
	}

	private string ConsumeUntil(string sub)
	{
		var endIdx = InputToEnd.IndexOf(sub, StringComparison.Ordinal);
		
		if (endIdx < 0)
		{
			var rest = InputToEnd;
			_pos = _input.Length;
			return rest;
		}
		
		var res = InputToEnd[..endIdx];
		_pos += endIdx;
		return res;
	}

	private string ConsumeAlphaNum()
	{
		var result = "";
		var c = InputToEnd[0];
		while (char.IsLetterOrDigit(c))
		{
			result += c;
			_pos++;
			c = InputToEnd[0];
		}

		return result;
	}

	private string ConsumeWithin(char border)
	{
		while (InputToEnd[0] != border)
			_pos++;
		_pos++;
		
		var result = "";
		var c = InputToEnd[0];
		
		while (c != border)
		{
			result += c;
			_pos++;
			c = InputToEnd[0];
		}
		_pos++;

		return result;
	}

	private LexerFunc LexIdentifierOrConstantOrOpen()
	{
		// Constant: 'Text', "Text", 1, or (
		SkipWhitespace();
		var first = InputToEnd[0];
		if (first is '\'') return LexConstantText;
		if (first is '(') return LexOpenPrecedence;
		var isNum = int.TryParse(first.ToString(), out _);
		if (isNum) return LexConstantNum;
		
		// Identifier: Text
		return LexIdentifier;
	}

	private LexerFunc LexOpenPrecedence()
	{
		var c = InputToEnd[0];
		_tokens.Add(new Token(TokenType.PrecedenceOpen, c.ToString()));
		_pos++;
		return LexIdentifierOrConstantOrOpen;
	}

	private LexerFunc LexIdentifier()
	{
		var name = ConsumeAlphaNum();
		_tokens.Add(new Token(TokenType.Identifier, name));
		return LexExpressionOrClose;
	}

	private LexerFunc LexConstantNum()
	{
		var num = ConsumeAlphaNum();
		_tokens.Add(new Token(TokenType.ConstantNum, num));
		return LexExpressionOrClose;
	}
	
	private LexerFunc LexConstantText()
	{
		var text = ConsumeWithin('\'');
		var textTrimmed = text.Trim('\'');
		_tokens.Add(new Token(TokenType.ConstantText, textTrimmed));
		return LexExpressionOrClose;
	}

	private LexerFunc LexExpressionOrClose()
	{
		SkipWhitespace();
		var first = InputToEnd[0];
		if (first is ')') return LexClosePrecedence;
		return LexExpression;
	}

	private LexerFunc LexClosePrecedence()
	{
		var c = InputToEnd[0];
		_tokens.Add(new Token(TokenType.PrecedenceClose, c.ToString()));
		_pos++;
		return LexExpression;
	}

	private LexerFunc LexExpression()
	{
		var expr = ConsumeAlphaNum();
		_tokens.Add(new Token(TokenType.Expression, expr));
		return LexIdentifierOrConstantOrOpen;
	}
}

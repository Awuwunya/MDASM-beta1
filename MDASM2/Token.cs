using System;
using System.Collections.Generic;

namespace MDASM2 {
	public abstract class Token {
		public string File;
		public int Line;
	}



	#region Token Values
	public class TokenValue : Token {
		public dynamic Value;
		public TokenValueType Type;

		public TokenValue(TokenValueType type, dynamic value) {
			Type = type;
			Value = value;
		}
	}

	public enum TokenValueType {
		None, Text, String,
		Float = 0x3F, Uint8 = 0x40, Int8, Uint16, Int16, Uint32, Int32, Uint64, Int64,
	}

	public class TokenEquate : TokenValue {
		public string Name;

		public TokenEquate(string name, TokenValueType type, dynamic value) : base(type, (object)value) {
			Name = name;
		}
	}

	public class TokenLabelCreate : TokenValue {
		public Token Name;

		public TokenLabelCreate(Token name) : base(TokenValueType.Text, null) {
			Name = name;
		}
	}

	public class TokenMacro : TokenEquate {
		public string[] ArgNames;

		public TokenMacro(string name) : base(name, TokenValueType.None, 0) {
		}
	}

	public class TokenMacroCall : TokenEquate {
		public List<TokenValue> Arguments;
		public string Size = "";

		public TokenMacroCall(string name) : base(name, TokenValueType.None, 0) {
			Arguments = new List<TokenValue>();
		}
	}
	#endregion

	#region Operators
	public abstract class TokenOperator : TokenValue {
		public bool IsUnary;
		public TokenValue Right;

		public TokenOperator() : base(TokenValueType.None, null) {
			IsUnary = true;
		}

		public abstract TokenValue Calculate();
		public virtual bool IsFull() => Right != null;
	}

	public abstract class TokenOperatorBinary : TokenOperator {
		public TokenValue Left;

		public TokenOperatorBinary() : base() {
			IsUnary = false;
		}

		public override bool IsFull() => Right != null && Left != null;
	}

	public class TokenOpSet : TokenOperatorBinary {
		public TokenOpSet() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}
	#endregion

	#region Operators Unary
	public class TokenOpNeg : TokenOperator {
		public TokenOpNeg() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpNot : TokenOperator {
		public TokenOpNot() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpLogicalNot : TokenOperator {
		public TokenOpLogicalNot() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}
	#endregion

	#region Operators Binary Basic
	public class TokenOpAdd : TokenOperatorBinary {
		public TokenOpAdd() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpSub : TokenOperatorBinary {
		public TokenOpSub() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpMultiply : TokenOperatorBinary {
		public TokenOpMultiply() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpDivide : TokenOperatorBinary {
		public TokenOpDivide() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpModulo : TokenOperatorBinary {
		public TokenOpModulo() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}
	#endregion

	#region Operators Binary Bitwise
	public class TokenOpAnd : TokenOperatorBinary {
		public TokenOpAnd() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpOr : TokenOperatorBinary {
		public TokenOpOr() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpXor : TokenOperatorBinary {
		public TokenOpXor() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}
	#endregion

	#region Operators Binary Shift
	public class TokenOpShiftLeft : TokenOperatorBinary {
		public TokenOpShiftLeft() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpShiftRight : TokenOperatorBinary {
		public TokenOpShiftRight() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpRotateLeft : TokenOperatorBinary {
		public TokenOpRotateLeft() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpRotateRight : TokenOperatorBinary {
		public TokenOpRotateRight() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}
	#endregion

	#region Operators Binary Comparison
	public class TokenOpEquals : TokenOperatorBinary {
		public TokenOpEquals() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpNotEquals : TokenOperatorBinary {
		public TokenOpNotEquals() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpLessThan : TokenOperatorBinary {
		public TokenOpLessThan() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpGreaterThan : TokenOperatorBinary {
		public TokenOpGreaterThan() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpLessOrEquals : TokenOperatorBinary {
		public TokenOpLessOrEquals() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}

	public class TokenOpGreaterOrEquals : TokenOperatorBinary {
		public TokenOpGreaterOrEquals() : base() { }

		public override TokenValue Calculate() {
			return null;
		}
	}
	#endregion
}
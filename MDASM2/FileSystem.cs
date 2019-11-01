using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MDASM2 {
	public class FileSystem {
		#region Misc
		public static Dictionary<string, FileData> Files = new Dictionary<string, FileData>();

		public static void Create(string filename, Stream data) {
			Files[filename] = new FileData(filename);
			Tokenize(Files[filename], data);
			data.Close();
			data.Dispose();
		}

		public static FileData Get(string filename) {
			try {
				if (!Files.ContainsKey(filename))
					Create(filename, new FileStream(filename, FileMode.Open));

			} catch (FileNotFoundException) {
				Program.Error("File " + filename + " could not be found!");
			}

			return Files[filename];
		}
		#endregion

		#region Tokenize
		private static void Tokenize(FileData data, Stream file) {
			// variables for processing the buffer
			byte[] buffer = new byte[1024];
			int len = 0;

			// variables for creating a text buffer
			char[] textbuf = new char[1024];
			int tpos = 0;

			// variables for special behaviour
			char strchar = '\0';
			bool write = true, escaped = false;

			// other variables
			int line = 0, strline = 0;

			while (true) {
				// read some data from the input file
				if ((len = file.Read(buffer, 0, buffer.Length)) <= 0) break;

				for (int i = 0;i < len;i++) {
					// if necessary, reallocate a larger array
					if (tpos >= textbuf.Length) {
						char[] _b = textbuf;
						textbuf = new char[textbuf.Length << 1];
						Array.Copy(_b, textbuf, tpos);
					}

					if (buffer[i] == '\n') {
						escaped = false;
						line++;

						if (strchar == 0) {
							if (tpos > 0) {
								// process the tokens here
								textbuf[tpos++] = '\0';
								ProcToken(data, ref textbuf, tpos, line);
							}

							// reset variables
							tpos = 0;
							write = true;
						}

					} else if (buffer[i] == ';' && strchar == 0) {
						write = escaped = false;

					} else if (buffer[i] == '\\' && strchar != 0) {
						escaped ^= true;
						textbuf[tpos++] = '\\';

					} else if (write && (buffer[i] == '\'' || buffer[i] == '"')) {
						// special string handling
						if (!escaped) {
							if (strchar == 0) {
								strchar = (char)buffer[i];
								strline = line;

							} else if (strchar == (char)buffer[i]) strchar = '\0';
						}

						textbuf[tpos++] = (char)buffer[i];
						escaped = false;

					} else if (write) {
						//	if(buffer[i] != ' ' && buffer[i] != '\t' && buffer[i] != '\r')
						textbuf[tpos++] = (char)buffer[i];
						escaped = false;
					}
				}
			}

			if (strchar != 0)
				Program.Error(data.FileName, line, "String starting at line " + strline + " not terminated!");

			// output the last line
			if (tpos > 0) {
				textbuf[tpos++] = '\0';

				// delay until we can overwrite data
				ProcToken(data, ref textbuf, tpos, line);
			}
		}

		private static void ProcToken(FileData data, ref char[] buf, int tpos, int line) {
			/****************************************
			// --------------- step 1 ---------------
			// gparse text
			*****************************************/

			TokenInfo[] infos = new TokenInfo[64];
			int ifp = 0;
			
			{
				TokenInfoEnum current = TokenInfoEnum.None;
				int start = -1, maybe = -1;
				bool? validlable = null;
				bool labelset = false;

				// variables for strings
				char strchar = '\0';
				bool escaped = false;

				for (int i = 0;i < tpos;i++) {
					if (ifp >= infos.Length) infos = ExpandInfos(infos);

					switch (buf[i]) {
						case '"': case '\'':
							// string delimiters, check what to do
							if (!escaped) {
								if (strchar == 0) {
									if (current != TokenInfoEnum.None)
										infos[ifp++] = new TokenInfo(current, start, i - 1);

									strchar = buf[i];
									start = i + 1;
									current = TokenInfoEnum.String;

								} else if (strchar == buf[i]) {
									strchar = '\0';
									infos[ifp++] = new TokenInfo(TokenInfoEnum.String, start, i);
									current = TokenInfoEnum.None;
								}
							}
							break;

						case '\\':
							// escaping character special behaviour
							if (strchar != 0) escaped ^= true;
							break;

						case ' ': case '\t': case '\r': case '\0':
							// space and end characters close statements
							if (strchar == 0 && current != TokenInfoEnum.None) {
								infos[ifp++] = new TokenInfo(current, start, i - 1);
								current = TokenInfoEnum.None;

								// make sure the next call wont overflow the array
								if (ifp >= infos.Length) infos = ExpandInfos(infos);
							}

							if (validlable == true) {
								validlable = false;
								maybe = ifp;
								infos[ifp++] = new TokenInfo(TokenInfoEnum.LabelOperatorMaybe, i, i);

							} else if (ifp > 0 && infos[ifp - 1].Type != TokenInfoEnum.Separator) {
								infos[ifp++] = new TokenInfo(TokenInfoEnum.Separator, i, i);
							}
							break;

						case ':':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								// create label operator
								if (i == 0) Program.Error(data.FileName, line, "Invalid empty label!");

								// check if the next character is :
								if(i + 1 < tpos && buf[i + 1] == ':') {
									i++;
									infos[ifp++] = new TokenInfo(TokenInfoEnum.CastOperator, i - i, i);

								} else if(!labelset) {
									validlable = false;
									labelset = true;
									infos[ifp++] = new TokenInfo(TokenInfoEnum.LabelOperator, i, i);

									// change LableOperatorMaybe to Separator
									if(maybe > 0) infos[maybe].Type = TokenInfoEnum.Separator;
									maybe = -1;

								} else Program.Error(data.FileName, line, "Can not have multiple labels on a line!");
							}
							break;

						case '=':
							if (ifp > 0 && infos[ifp - 1].Type == TokenInfoEnum.MathOperator && infos[ifp - 1].Start == infos[ifp - 1].End) {
								if (buf[infos[ifp - 1].End] == '!' || buf[infos[ifp - 1].End] == '=' || buf[infos[ifp - 1].End] == '<' || buf[infos[ifp - 1].End] == '>') {
									infos[ifp - 1] = new TokenInfo(TokenInfoEnum.MathOperator, infos[ifp - 1].Start, i);
									break;
								}
							}
							goto case '+';

						case '<':
							if (ifp > 0 && infos[ifp - 1].Type == TokenInfoEnum.MathOperator && infos[ifp - 1].Start >= infos[ifp - 1].End - 1 && buf[infos[ifp - 1].End] == '<') {
								infos[ifp - 1] = new TokenInfo(TokenInfoEnum.MathOperator, infos[ifp - 1].Start, i);
								break;
							}
							goto case '+';

						case '>':
							if (ifp > 0 && infos[ifp - 1].Type == TokenInfoEnum.MathOperator && infos[ifp - 1].Start >= infos[ifp - 1].End - 1 && buf[infos[ifp - 1].End] == '>') {
								infos[ifp - 1] = new TokenInfo(TokenInfoEnum.MathOperator, infos[ifp - 1].Start, i);
								break;
							}
							goto case '+';

						case '!': case '+': case '-': case '*': case '/': case '%': case '&': case '|': case '^': case '~':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.MathOperator, i, i);
							}
							break;

						case '(':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.OpenParen, i, i);
							}
							break;

						case ')':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.CloseParen, i, i);
							}
							break;

						case '[':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.OpenSqu, i, i);
							}
							break;

						case ']':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.CloseSqu, i, i);
							}
							break;

						case '{':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.BlockStart, i, i);
							}
							break;

						case '}':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.BlockEnd, i, i);
							}
							break;

						case '.':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.Dot, i, i);
							}
							break;

						case ',':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i - 1);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.Comma, i, i);
							}
							break;

						default:
							if (strchar == 0) {
								if (current == TokenInfoEnum.None) {
									// check if we can start a new number or text sequence
									if (buf[i] == '$' || (buf[i] >= '0' && buf[i] <= '9')) {
										start = i;
										current = TokenInfoEnum.Number;

									} else if (buf[i] == '_' || ((buf[i] & 0xDF) >= 'A' && (buf[i] & 0xDF) <= 'Z')) {
										start = i;
										current = TokenInfoEnum.Text;
										validlable = i == 0 ? (bool?)true : null;

									} else {
										Program.Error(data.FileName, line, "Character " + buf[i] + " not recognized!");
									}
								}
							}
							break;
					}
				}
			}

			/****************************************
			// --------------- step 2 ---------------
			// generate more proper tokens
			*****************************************/

			TokenGroup[] groups = new TokenGroup[32];
			int igp = 0;

			{
				// convert tokens from text into proper format
				int istart = 0, depth = 0, i = 0;
				Stack<int> macrodep = new Stack<int>();
				Stack<int> squdep = new Stack<int>();

			begin:
				if (i >= ifp) goto done;

				switch (infos[i].Type) {
					case TokenInfoEnum.None: case TokenInfoEnum.Separator:
						i++;
						goto begin;

					case TokenInfoEnum.LabelOperator: case TokenInfoEnum.LabelOperatorMaybe:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.Label, depth, null);
						i++;
						goto begin;

					case TokenInfoEnum.CastOperator:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.Cast, depth, null);
						i++;
						goto begin;

					case TokenInfoEnum.BlockStart:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.BlockStart, depth, null);
						i++;
						goto begin;

					case TokenInfoEnum.BlockEnd:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.BlockEnd, depth, null);
						i++;
						goto begin;

					case TokenInfoEnum.OpenParen:
						i++; depth++;
						goto begin;

					case TokenInfoEnum.CloseParen:
						i++;
						if(--depth < 0) {
							Program.Error(data.FileName, line, "Unexpected closing parenthesis!");
							break;
						}

						if(macrodep.Count > 0 && macrodep.Peek() == depth) {
							macrodep.Pop();
							GroupExpand(igp, ref groups);
							groups[igp++] = new TokenGroup(TokenGroupEnum.MacroEnd, depth, null);
						}
						goto begin;

					case TokenInfoEnum.OpenSqu:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.ArrayStart, depth, null);

						i++;
						squdep.Push(depth++);
						goto begin;

					case TokenInfoEnum.CloseSqu:
						if(squdep.Count > 0 && squdep.Peek() == --depth) {
							squdep.Pop();
							GroupExpand(igp, ref groups);
							groups[igp++] = new TokenGroup(TokenGroupEnum.ArrayEnd, depth, null);

						} else Program.Error(data.FileName, line, "Unexpected closing square bracket!");
						i++;
						goto begin;

					case TokenInfoEnum.Text:
						goto type_text;

					case TokenInfoEnum.String:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.String, depth, CutStr(ref buf, infos[i++]));
						goto begin;

					case TokenInfoEnum.Number:
						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.Number, depth, Program.StringToNum(CutStr(ref buf, infos[i++])));
						goto begin;

					case TokenInfoEnum.MathOperator: {
						TokenOperator op = null;

						switch(infos[i].End - infos[i].Start) {
							case 0: {
								// 1-character operators
								op = buf[infos[i].Start] switch
								{
									'+' => new TokenOpAdd(),
									'-' => new TokenOpSub(),
									'*' => new TokenOpMultiply(),
									'/' => new TokenOpDivide(),
									'%' => new TokenOpModulo(),

									'&' => new TokenOpModulo(),
									'|' => new TokenOpModulo(),
									'^' => new TokenOpModulo(),

									'=' => new TokenOpSet(),
									'!' => new TokenOpLogicalNot(),
									'<' => new TokenOpLessThan(),
									'>' => new TokenOpGreaterThan(),

									_ => null
								};
							}
							break;

							case 1: {
								// 2-character operators
								switch(buf[infos[i].Start]) {
									case '=':
										if('=' == buf[infos[i].Start + 1])
											op = new TokenOpEquals();
										break;

									case '!':
										if('=' == buf[infos[i].Start + 1])
											op = new TokenOpNotEquals();
										break;

									case '<':
										op = buf[infos[i].Start + 1] switch
										{
											'=' => new TokenOpLessOrEquals(),
											'<' => new TokenOpShiftLeft(),
											'>' => new TokenOpNotEquals(),
											_ => null
										};
										break;

									case '>':
										op = buf[infos[i].Start + 1] switch
										{
											'=' => new TokenOpGreaterOrEquals(),
											'>' => new TokenOpShiftRight(),
											_ => null
										};
										break;
								}
							}
							break;

							case 2: {
								// 3-character operators
								switch(buf[infos[i].Start]) {
									case '<':
										if(buf[infos[i].Start + 1] == '<' && buf[infos[i].Start + 2] == '<')
											op = new TokenOpRotateLeft();
										break;

									case '>':
										if(buf[infos[i].Start + 1] == '>' && buf[infos[i].Start + 2] == '>')
											op = new TokenOpRotateRight();
										break;
								}
							}
							break;
						}

						if(op == null) {
							Program.Error(data.FileName, line, "Unexpected operator encountered: " + CutStr(ref buf, infos[i++]));
							break;
						}

						GroupExpand(igp, ref groups);
						groups[igp++] = new TokenGroup(TokenGroupEnum.MathOperator, depth, op);
						i++;
					}
					break;

					default:
						Program.Error(data.FileName, line, "Unexpected token encountered: " + infos[i].Type);
						break;
				}
				goto begin;

			type_text:// this is when we encounter Text
				{
					string label = CutStr(ref buf, infos[i]), dot = null;

					text_dot:
					if (++i >= ifp)
						if (++i >= ifp) goto put_label;

					// check what we should do with the value
					if (infos[i].Type == TokenInfoEnum.OpenParen) {
						// this is a macro
						macrodep.Push(depth++);
						goto put_macro;


					} else if (infos[i].Type == TokenInfoEnum.Separator) {
						// try to see if we can make this to be a macro call
						if (i++ == 0 || i >= ifp) goto put_label;
						if (i - 2 != labelpos) goto put_label;

						// this is a macro
						macrodep.Push(-1);
						depth++;
						goto put_macro;

					} else if (dot == null) {
						if (infos[i].Type == TokenInfoEnum.Dot) {
							// special handling for the macro size parameter. It may be also empty.
							dot = "";
							if (++i >= ifp) goto done;

							if (infos[i].Type == TokenInfoEnum.Text || infos[i].Type == TokenInfoEnum.Number)
								dot = CutStr(ref buf, infos[i++]);

							goto text_dot;

						}
					}

				put_label: // we found some type of text...
					GroupExpand(igp + (dot == null ? 0 : 1), ref groups);
					groups[igp++] = new TokenGroup(TokenGroupEnum.Text, depth, label);

					if(dot != null) groups[igp++] = new TokenGroup(TokenGroupEnum.Local, depth, dot);
					goto begin;

				put_macro:  // this is a macro call
					GroupExpand(igp + (dot == null ? 0 : 1), ref groups);
					groups[igp++] = new TokenGroup(TokenGroupEnum.MacroName, depth, label);

					if (dot != null) groups[igp++] = new TokenGroup(TokenGroupEnum.MacroSize, depth, dot);
					goto begin;
				}

				done:;
			}

			/****************************************
			// --------------- step 3 ---------------
			// combine into recursive interest groups
			*****************************************/

			{
			}
		}

		// cut part of the buffer into a string according to token
		private static string CutStr(ref char[] buf, TokenInfo t) {
			return new string(buf, t.Start, t.End - t.Start + 1);
		}

		private static void GroupExpand(int expand, ref TokenGroup[] groups) {
			if(expand >= groups.Length) {
				var _g = groups;
				groups = new TokenGroup[_g.Length << 1];
				Array.Copy(_g, groups, _g.Length);
			}
		}

		// quick method to double the infos array size
		private static TokenInfo[] ExpandInfos(TokenInfo[] infos) {
			TokenInfo[] ret = new TokenInfo[infos.Length << 1];
			Array.Copy(infos, ret, infos.Length);
			return ret;
		}
		#endregion
	}

	public enum TokenInfoEnum {
		None, Separator, String, Text, Number,
		OpenParen, CloseParen, OpenSqu, CloseSqu, BlockStart, BlockEnd,
		MathOperator, LabelOperator, LabelOperatorMaybe, CastOperator,
		Dot, Comma,
	}

	public struct TokenInfo {
		public TokenInfoEnum Type;
		public int Start, End;

		public TokenInfo(TokenInfoEnum type, int start, int end){
			Type = type;
			Start = start;
			End = end;
		}
	}

	public struct TokenGroup {
		public TokenGroupEnum Type;
		public dynamic Variable;
		public int Depth;

		public TokenGroup(TokenGroupEnum type, int depth, dynamic variable) {
			Type = type;
			Depth = depth;
			Variable = variable;
		}
	}

	public enum TokenGroupEnum {
		None, String, Text, Number, Local, Label, Cast,
		Expression, MathOperator,
		MacroName, MacroSize, MacroArgument, MacroEnd,
		ArrayStart, ArrayEnd, BlockStart, BlockEnd,
	}

	public class FileData {
		public List<Token> Tokens;
		public string FileName;

		public FileData(string filename) {
			FileName = filename;
			Tokens = new List<Token>();
		}
	}
}
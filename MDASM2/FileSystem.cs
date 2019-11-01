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
			TokenInfo[] infos = new TokenInfo[64];
			int ifp = 0;

			{
				TokenInfoEnum current = TokenInfoEnum.None;
				int start = -1;
				bool? validlable = null;

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
										infos[ifp++] = new TokenInfo(current, start, i);

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
								infos[ifp++] = new TokenInfo(current, start, i);
								current = TokenInfoEnum.None;

								// make sure the next call wont overflow the array
								if (ifp >= infos.Length) infos = ExpandInfos(infos);
							}

							if (validlable == true) {
								validlable = null;
								infos[ifp++] = new TokenInfo(TokenInfoEnum.LabelOperatorMaybe, i, i);

							} else if (ifp > 0 && infos[ifp - 1].Type != TokenInfoEnum.Separator) {
								infos[ifp++] = new TokenInfo(TokenInfoEnum.Separator, i, i);
							}
							break;

						case ':':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								// create label operator
								if (i == 0) Program.Error(data.FileName, line, "Invalid empty label!");
								infos[ifp++] = new TokenInfo(TokenInfoEnum.LabelOperator, i, i);

								if (validlable != null)
									validlable ^= true;
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
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
									infos[ifp++] = new TokenInfo(current, start, i);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.Comma, i, i);
							}
							break;

						case '#':
							if (strchar == 0) {
								if (current != TokenInfoEnum.None) {
									infos[ifp++] = new TokenInfo(current, start, i);
									current = TokenInfoEnum.None;

									// make sure the next call wont overflow the array
									if (ifp >= infos.Length) infos = ExpandInfos(infos);
								}

								infos[ifp++] = new TokenInfo(TokenInfoEnum.Hash, i, i);
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

			TokenGroup[] groups = new TokenGroup[32];
			int igp = 0, labelpos = -1;

			{
				// get the label position for later
				for(int i = 0;i < ifp;i++) {
					if(infos[i].Type == TokenInfoEnum.LabelOperatorMaybe) {
						labelpos = i;
						break;

					} else if(infos[i].Type == TokenInfoEnum.LabelOperator) {
						if(ifp > ++i || infos[i].Type != TokenInfoEnum.LabelOperator) {
							labelpos = i - 1;
							break;
						}
					}
				}
			}

			{
				// group tokens into larger interest groups
				int istart = 0, depth = 0, i = 0;
				Stack<int> macrodep = new Stack<int>();

				begin:
				if (i >= ifp) goto done;

				switch (infos[i].Type) {
					case TokenInfoEnum.None: case TokenInfoEnum.Separator:
						i++;
						goto begin;


					case TokenInfoEnum.Text:
						goto type_text;

					case TokenInfoEnum.String:
						if (igp >= groups.Length) {
							TokenGroup[] _g = groups;
							groups = new TokenGroup[_g.Length << 1];
							Array.Copy(_g, groups, _g.Length);
						}

						groups[igp++] = new TokenGroup(TokenGroupEnum.String, depth, new string(buf, infos[i].Start, infos[i++].End));
						goto begin;

					case TokenInfoEnum.Number:
						groups[igp++] = new TokenGroup(TokenGroupEnum.Number, depth, Program.StringToNum(new string(buf, infos[i].Start, infos[i++].End)));
						goto begin;

					default:
						Program.Error(data.FileName, line, "Unexpected token encountered: " + infos[i].Type);
						break;
				}


				type_text:// this is when we encounter Text
				{
					string label = new string(buf, infos[i].Start, infos[i].End), dot = null;

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
								dot = new string(buf, infos[i].Start, infos[i].End - infos[i++].Start);

							goto text_dot;

						}
					}

					put_macro:
					if (igp + (dot == null ? 0 : 1) >= groups.Length) {
						TokenGroup[] _g = groups;
						groups = new TokenGroup[_g.Length << 1];
						Array.Copy(_g, groups, _g.Length);
					}

					groups[igp++] = new TokenGroup(TokenGroupEnum.MacroName, depth, label);
					if (dot != null) groups[igp++] = new TokenGroup(TokenGroupEnum.MacroSize, depth, dot);
					goto begin;

					put_label:
					// we found some type of text...
					if (igp + (dot == null ? 0 : 1) >= groups.Length) {
						TokenGroup[] _g = groups;
						groups = new TokenGroup[_g.Length << 1];
						Array.Copy(_g, groups, _g.Length);
					}

					groups[igp++] = new TokenGroup(TokenGroupEnum.Text, depth, label);
					if (dot != null) groups[igp++] = new TokenGroup(TokenGroupEnum.Local, depth, dot);
					goto begin;
				}

				done:;
			}

			{
				/*	type_text:// this is when we encounter Text at the beginning
					{
						_value = new TokenValue(TokenValueType.Text, new string(buf, infos[i].Start, infos[i].End - infos[i].Start));
						string size = null;
						if (++i >= ifp) goto done;

						text_size:
						if (infos[i].Type == TokenInfoEnum.OpenParen) {
							// this is a direct macro call with parenthesis, try processing it
							macros.Push(new TokenMacroCall(_value.Value as string));
							priority.Push(macros.Peek());

							_value = null;
							i++;
							depth++;
							nextarg = true;
							goto begin;

						} else if (infos[i].Type == TokenInfoEnum.Separator) {
							if (macros.Count != 0) {
								if (i == ifp - 1) goto begin;
								else goto error;
							}

							// this is a direct macro call without parenthesis, try processing it
							macros.Push(new TokenMacroCall(_value.Value as string));
							priority.Push(macros.Peek());

							_value = null;
							i++;
							depth++;
							parenmacro = false;
							nextarg = true;
							goto begin;

						} else if (size == null) {
							if (infos[i].Type == TokenInfoEnum.Dot) {
								// special handling for the macro size parameter. It may be also empty.
								size = "";
								if (++i >= ifp) goto done;

								if (infos[i].Type == TokenInfoEnum.Text || infos[i].Type == TokenInfoEnum.Number)
									size = new string(buf, infos[i].Start, infos[i].End - infos[i++].Start);

								goto text_size;
							} else if (infos[i].Type == TokenInfoEnum.MathOperator)
								goto math;

							else if (infos[i].Type == TokenInfoEnum.OpenSqu)
								goto square;

							else if (infos[i].Type == TokenInfoEnum.BlockStart)
								goto block;

							else if (infos[i].Type == TokenInfoEnum.Hash)
								goto hash;

							else if (infos[i].Type == TokenInfoEnum.Comma)
								goto type_comma;

							else if (infos[i].Type == TokenInfoEnum.LabelOperator)
								goto type_lable;

							else if (infos[i].Type == TokenInfoEnum.LabelOperatorMaybe)
								goto type_lable2;

							else goto error;
						} else goto error;
					}

					// variables for token processing
					TokenValue _value = null;
					Stack<TokenMacroCall> macros = new Stack<TokenMacroCall>();
					Stack<Token> priority = new Stack<Token>();

					
					bool parenmacro = true, nextarg = false;
					int depth = 0, i = 0;

					// process into tokens
					begin:
					if (i >= ifp) goto done;

					switch (infos[i].Type) {
						case TokenInfoEnum.None: case TokenInfoEnum.Separator:
							i++;
							goto begin;

						case TokenInfoEnum.OpenParen:
							depth++;
							i++;
							goto begin;

						case TokenInfoEnum.CloseParen:
							depth--;
							i++;
							goto begin;

						case TokenInfoEnum.Text:
							goto type_text;

						case TokenInfoEnum.String:
							goto type_string;

						case TokenInfoEnum.Number:
							goto type_number;

						case TokenInfoEnum.Comma:
							goto type_comma;

						case TokenInfoEnum.MathOperator:
							goto math;

						default:
							goto error;
					}

					type_comma:
					// check if the priority item is a macro
					if (priority.Count == 0 || !(priority.Peek() is TokenMacroCall p))
						goto error;

					// if arguemnt is empty, add it here
					if (nextarg) p.Arguments.Add(new TokenValue(TokenValueType.Text, ""));

					nextarg = true;
					i++;
					goto begin;

					type_string:
					_value = new TokenValue(TokenValueType.String, new string(buf, infos[i].Start, infos[i].End - infos[i++].Start));
					goto value_put;

					type_number: // TODO: Finish
					_value = new TokenValue(TokenValueType.Int64, new string(buf, infos[i].Start, infos[i].End - infos[i++].Start));
					goto value_put;

					value_put:// code flows here to put a value into the current priority object in the stack. The code handles each case correctly
					{
						if (priority.Count == 0) goto error;
						if (_value == null) goto error;

						if (priority.Peek() is TokenMacroCall _m) {
							// check if we can add another argument
							if (!nextarg) goto error;
							nextarg = false;

							// priority item is a macro call, process what we should do here.
							_m.Arguments.Add(_value);
							_value = null;

						} else if(priority.Peek() is TokenOperator _o) {
							if (_o.Right != null) goto error;

							// this is an operator, see what we can do about it
							_o.Right = _value;
							_value = null;
							goto priority_unwind;
						}

						goto begin;
					}

					priority_unwind:// we go here when we want to remove all unnecessary items from priority queue!
					if(priority.Count > 0) {
						if(priority.Peek() is TokenOperator _o && _o.IsFull()) {

							// remove full operators from the stack.
							priority.Pop();
							goto priority_unwind;
						}
					}

					goto begin;

					type_lable2:
					++i;
					goto type_lable3;

					type_lable:// this is when we encounter a lable operator
					if (++i < ifp && infos[i].Type == TokenInfoEnum.LabelOperator)
						goto type_cast;

					type_lable3:
					// this is a label, make sure we can create the label correctly
					if (priority.Count > 0) goto error;

					if(_value != null) {
						data.Tokens.Add(new TokenLabelCreate(_value));
						_value = null;

					} else {
						if(data.Tokens.Count == 0) goto error;

						// we can only assume the last entry is supposed to be the value we want.
						Token t = data.Tokens[data.Tokens.Count - 1];
						data.Tokens.Remove(t);

						// cause an error to occur if the type is wrong
						if(t is TokenLabelCreate) goto error;

						data.Tokens.Add(new TokenLabelCreate(t));
					}

					goto begin;

					type_cast:// this is when we encounter two label operators
					goto unimpl;

					square:// this is when we encounter a square operator
					goto unimpl;

					block:// this is when we encounter a block start
					goto unimpl;

					hash:// this is when we encounter a hash
					goto unimpl;

					math:// this is when we encounter math operator at the beginning
					{
						TokenOperator op;

						switch (buf[infos[i++].Start]) {
							case '+': op = new TokenOpAdd(); break;
							case '-': op = new TokenOpSub(); break;
							case '*': op = new TokenOpMultiply(); break;
							case '/': op = new TokenOpDivide(); break;
							case '%': op = new TokenOpModulo(); break;

							case '&': op = new TokenOpAnd(); break;
							case '|': op = new TokenOpOr(); break;
							case '^': op = new TokenOpXor(); break;

							case '=':
								if (i >= ifp || infos[i].Type != TokenInfoEnum.MathOperator || buf[infos[i].Start] != '=') {
									++i;
									op = new TokenOpSet();

								} else op = new TokenOpEquals();
								break;

							case '!':
								if (i >= ifp || infos[i].Type != TokenInfoEnum.MathOperator || buf[infos[i].Start] != '=') {
									++i;
									op = new TokenOpLogicalNot();

								} else op = new TokenOpNotEquals();
								break;

							case '<':
								if (i < ifp && infos[i].Type == TokenInfoEnum.MathOperator) {
									if (buf[infos[i].Start] == '=') {
										++i;
										op = new TokenOpLessOrEquals();

									} else if (buf[infos[i].Start] == '<') {
										if (++i >= ifp || infos[i].Type != TokenInfoEnum.MathOperator || buf[infos[i].Start] != '<') {
											++i;
											op = new TokenOpRotateLeft();

										} else op = new TokenOpShiftLeft();

									} else op = new TokenOpLessThan();
								} else op = new TokenOpLessThan();
								break;

							case '>':
								if (i < ifp && infos[i].Type == TokenInfoEnum.MathOperator) {
									if (buf[infos[i].Start] == '=') {
										++i;
										op = new TokenOpGreaterOrEquals();

									} else if (buf[infos[i].Start] == '>') {
										if (++i >= ifp || infos[i].Type != TokenInfoEnum.MathOperator || buf[infos[i].Start] != '>') {
											++i;
											op = new TokenOpRotateRight();

										} else op = new TokenOpShiftRight();

									} else op = new TokenOpGreaterThan();
								} else op = new TokenOpGreaterThan();
								break;

							default:
								goto unimpl;
						}

						// now that we know what operation to use, we have to somehow make it work! Yay...
						if (op.IsUnary) {
							if (_value != null) goto error;

							if (priority.Count > 0) {
								// add this thing to priority things
								if (priority.Peek() is TokenMacroCall _m) {
									if (!nextarg) goto error;
									nextarg = false;

									// priority item is a macro call, process what we should do here.
									_m.Arguments.Add(op);
									_value = null;
									priority.Push(op);
								}

							} else goto error;
						} else {
							TokenOperatorBinary bop = op as TokenOperatorBinary;

							if (_value != null) {
								bop.Left = _value;
								_value = null;
								priority.Push(op);

							} else if (priority.Count > 0) {
								if (priority.Peek() is TokenOperator o && !o.IsFull())
									goto error;

								// we can successfully stitch these 2 things together
								if (priority.Peek() is TokenValue v) {
									bop.Left = v;
									priority.Pop();
									priority.Push(op);

								} else goto error;

							} else if (data.Tokens.Count > 0) {
								if(data.Tokens[data.Tokens.Count - 1] is TokenLabelCreate c) {
									// the last item is a label, process that
									bop.Left = c;
									data.Tokens.Remove(c);
									data.Tokens.Add(op);
									priority.Push(op);

								} else if (data.Tokens[data.Tokens.Count - 1] is TokenValue v) {
									// the last item is a value
									bop.Left = v;
									data.Tokens.Remove(v);
									data.Tokens.Add(op);
									priority.Push(op);

								} else goto error;

							} else goto error;
						}
						goto begin;
					}

					error:// execution falls here in case of some unexpected token execution
					Program.Error(data.FileName, line, "Unexpected token encountered: " + infos[i].Type);

					unimpl:
					Program.Error(data.FileName, line, "Not implemented: " + infos[i].Type);

					done:// we are done! But first, check that the result is valid....
					if(priority.Count > 0) {

						// we have priority items to sort. Make sure its correct
						if (priority.Peek() is TokenMacroCall p_) {
							if (!parenmacro && macros.Count == 1 && macros.Peek() == priority.Peek()) {

								// if arguemnt is empty, add it here
								if (nextarg) p_.Arguments.Add(new TokenValue(TokenValueType.Text, ""));

								// if the last macro was parenthesisless macro, there is only 1 macro, and it happens to be the same macro as the priority macro, pop it from the stack
								data.Tokens.Add(macros.Pop());
								priority.Pop();
								goto done;

							} else goto error;
						} else goto error;
					}
					*/
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
		MathOperator, LabelOperator, LabelOperatorMaybe,
		Dot, Comma, Hash,
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
		None, String, Text, Number, Local,
		Expression, MathOperator,
		MacroName, MacroSize, MacroArgument, MacroEnd,
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
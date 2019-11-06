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
				data.Tokens.AddRange(ProcToken(data, ref textbuf, tpos, line));
			}
		}

		private static Token[] ProcToken(FileData data, ref char[] buf, int tpos, int line) {
			/****************************************
			// --------------- step 1 ---------------
			// parse text into tokens
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

			TokenGroup parent = new TokenGroup(TokenNormalEnum.None, -1, null);

			{
				// convert tokens from text into proper format
				Stack<TokenGroup> curs = new Stack<TokenGroup>();
				curs.Push(parent);

				int depth = 0, i = 0, maxdep = 0;
				Stack<int> macrodep = new Stack<int>();
				Stack<int> squdep = new Stack<int>();

			begin:
				if (i >= ifp) goto done;

				switch (infos[i].Type) {
					case TokenInfoEnum.None: case TokenInfoEnum.Separator:
						i++;
						goto begin;

					case TokenInfoEnum.LabelOperator: case TokenInfoEnum.LabelOperatorMaybe:
						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.Label, depth, null));
						i++;
						goto begin;

					case TokenInfoEnum.CastOperator:
						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.Operator, depth, new TokenOpCast()));
						i++;
						goto begin;

					case TokenInfoEnum.BlockStart:
						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.BlockStart, depth, null));
						i++;
						goto begin;

					case TokenInfoEnum.BlockEnd:
						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.BlockEnd, depth, null));
						i++;
						goto begin;

					case TokenInfoEnum.OpenParen: {
						i++;
						if(++depth > maxdep) maxdep = depth;

						TokenGroup g = new TokenGroup(TokenNormalEnum.None, depth, null);
						curs.Peek().AddChild(g);
						curs.Push(g);
					}
					goto begin;

					case TokenInfoEnum.CloseParen:
						i++;
						if(--depth < 0) {
							Program.Error(data.FileName, line, "Unexpected closing parenthesis!");
							break;
						}

						if(macrodep.Count > 0 && macrodep.Peek() == depth) {
							macrodep.Pop();
							curs.Pop();
						}
						goto begin;

					case TokenInfoEnum.OpenSqu: {
						TokenGroup g = new TokenGroup(TokenNormalEnum.Operator, depth, new TokenOpArray());
						curs.Peek().AddChild(g);
						curs.Push(g);

						i++;
						squdep.Push(depth++);
						if(depth > maxdep) maxdep = depth;
					}
					goto begin;

					case TokenInfoEnum.CloseSqu:
						if(squdep.Count > 0 && squdep.Peek() == --depth) {
							squdep.Pop();
							curs.Pop();

						} else Program.Error(data.FileName, line, "Unexpected closing square bracket!");
						i++;
						goto begin;

					case TokenInfoEnum.Text:
						goto type_text;

					case TokenInfoEnum.String:
						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.String, depth, new TokenValue(TokenValueType.String, CutStr(ref buf, infos[i++]))));
						goto begin;

					case TokenInfoEnum.Number:
						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.Number, depth, Program.StringToNum(CutStr(ref buf, infos[i++]))));
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

						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.Operator, depth, op));
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
						if(depth > maxdep) maxdep = depth;
						goto put_macro;


					} else if (infos[i].Type == TokenInfoEnum.Separator) {
						// try to see if we can make this to be a macro call
						if (i++ == 0 || i >= ifp) goto put_label;
					//	if (i - 2 != labelpos) goto put_label;

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
					if(dot != null) {
						// special dot handling
						TokenOpField f = new TokenOpField {
							Left = new TokenValue(TokenValueType.Text, label),
							Right = new TokenValue(TokenValueType.Text, dot)
						};

						curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.Operator, depth, f));

					} else curs.Peek().AddChild(new TokenGroup(TokenNormalEnum.Text, depth, new TokenValue(TokenValueType.Text, label)));
					goto begin;

				put_macro: { // this is a macro call
						TokenMacroCall c = new TokenMacroCall(label);
						TokenGroup g = new TokenGroup(TokenNormalEnum.MacroCall, depth, c);
						curs.Peek().AddChild(g);

						if(dot != null) c.Size = dot;
						depth++;
						curs.Push(g);
						goto begin;
					}
				}

				done:;
			}

			/****************************************
			// --------------- step 3 ---------------
			// combine groups into valid token
			*****************************************/

			{
				Stack<TokenGroup> curs = new Stack<TokenGroup>();
				curs.Push(parent);

				// search for the deepest child structure
				recurse:
				for(int i = 0;i < curs.Peek().Children.Count; i++) {
					if(curs.Peek().Children[i].Children.Count > 0) {
						curs.Push(curs.Peek().Children[i]);
						goto recurse;
					}
				}

			rerun:
				List<TokenGroup> ignore = new List<TokenGroup>();

				recheck:
				if(curs.Peek().Children.Count == 0) {
					curs.Pop();
					goto recurse;

				} else if(curs.Peek().Children.Count == 1) {
					// handle merging with parent element
					TokenGroup child = curs.Peek().Children[0];

					switch(curs.Peek().Type) {
						case TokenNormalEnum.None:
							TokenGroup pa = curs.Pop();
							if(curs.Count > 0)
								curs.Peek().Children[curs.Peek().Children.IndexOf(pa)] = child;

							else return new Token[] { pa.Variable as Token };	// return this as the statement
							break;

						case TokenNormalEnum.MacroCall:
							if(!child.IsValueOrOperator())
								Program.Error(data.FileName, line, "Invalid macro argument encountered!");

							(curs.Peek().Variable as TokenMacroCall).Arguments.Add(child.Variable);
							curs.Pop().Children.Remove(child);
							break;

						case TokenNormalEnum.Operator: {
							if(curs.Peek().Variable is TokenOpArray a) {
								a.Right = child.Variable as TokenValue;
								curs.Pop().Children.Remove(child);

							} else goto case default;
						} 
							break;

						default:
							Program.Error(data.FileName, line, "Invalid sequence encountered. Unable to continue.");
							break;
					}

					goto recurse;
				}

				// we need to intelligently combine each inner group
				int pric = -1, maxprec = int.MinValue;
				TokenGroup prec = null;

				// normal case with more than 1 children
				for(int i = 0;i < curs.Peek().Children.Count; i++) {
					int p = curs.Peek().Children[i].GetPrecedence();

					if(p > maxprec && !ignore.Contains(curs.Peek().Children[i]) && (curs.Peek().Children[i].Variable as TokenOperator)?.IsFull() != true) {
						maxprec = p;
						pric = i;
						prec = curs.Peek().Children[i];
					}
				}

				TokenGroup left = null, right = null;

				// check if this has inner children, and if so, process them correctly
				if(prec != null) {
					if(maxprec >= 0) {
						// ok, now we figure out what to do with this child
						if(pric > 0) left = curs.Peek().Children[pric - 1];
						if(pric + 1 < curs.Peek().Children.Count) right = curs.Peek().Children[pric + 1];

						// precaution
						if(left == null && right == null) {
							curs.Pop();
							goto recurse;
						}

						switch(prec.Type) {
							case TokenNormalEnum.Label:
								if(prec.Variable != null)
									goto nextone;

								if(left == null)
									Program.Error(data.FileName, line, "Invalid label!");

								prec.Variable = new TokenLabelCreate(left.Variable);
								curs.Peek().Children.Remove(left);
								break;

							case TokenNormalEnum.Operator: {
								bool change = false;

								// this is a math operator, do spechul stuff
								if(left == null || left.GetPrecedence() < 0) {
									// this ought to be unary operator...

									if(right == null || right.GetPrecedence() < 0)
										Program.Error(data.FileName, line, "Invalid left and right value!");

									if((prec.Variable as TokenOperator)?.IsUnary != true && (prec.Variable as TokenOperatorBinary)?.Left == null) {
										// its not! However, this may be a special case
										if(prec.Variable is TokenOpAdd) {
											curs.Peek().Children.Remove(prec);
											goto rerun;

										} else if(prec.Variable is TokenOpSub) prec.Variable = new TokenOpNeg();
										else Program.Error(data.FileName, line, "Unexpected binary operator " + (prec.Variable as TokenOperator)?.OpStr + " without a left value!");
									}

									// check if left is a valid value that we could assign
									if(right.IsValueOrOperator()) {
										if((prec.Variable as TokenOperator)?.Right != null) {
											if(right.Variable is TokenOperatorBinary r && r.Left == null) {
												r.Left = prec.Variable as TokenValue;
												curs.Peek().Children.Remove(prec);
												change = true;

											} else Program.Error(data.FileName, line, "Operators right side already has a value!");

										} else {
											(prec.Variable as TokenOperator).Right = right.Variable as TokenValue;
											curs.Peek().Children.Remove(right);
											change = true;

											if(right.Variable is TokenOperator o) {
												// check that there will be no orphan branches
												if(!o.IsUnary && (o as TokenOperatorBinary).Left == null)
													Program.Error(data.FileName, line, "Unexpected binary operator " + o.OpStr + " without a left value!");
											}
										}
									}

								} else {
									// this ought to be binary operator...

									// first, try to merge left value
									if(left.IsValueOrOperator()) {
										if((prec.Variable as TokenOperatorBinary)?.Left != null) {
											if(left.Variable is TokenOperator l && l.Right == null) {
												l.Right = prec.Variable as TokenValue;
												curs.Peek().Children.Remove(prec);
												change = true;

											} else Program.Error(data.FileName, line, "Operators left side already has a value!");

										} else {
											(prec.Variable as TokenOperatorBinary).Left = left.Variable as TokenValue;
											curs.Peek().Children.Remove(left);
											change = true;

											if(left.Variable is TokenOperator o) {
												// check that there will be no orphan branches
												if(!o.IsUnary && (o as TokenOperatorBinary).Right == null)
													Program.Error(data.FileName, line, "Unexpected binary operator " + o.OpStr + " without a right value!");
											}
										}
									}

									// special case for array operators
									if(!(prec.Variable is TokenOpArray) && right != null && right.GetPrecedence() >= 0) {
										// add only if valid
										if(right.IsValueOrOperator()) {
											if((prec.Variable as TokenOperatorBinary)?.Right != null) {
												if(right.Variable is TokenOperatorBinary r && r.Left == null) {
													r.Left = prec.Variable as TokenValue;
													curs.Peek().Children.Remove(prec);
													change = true;

												} else Program.Error(data.FileName, line, "Operators right side already has a value!");

											} else {
												(prec.Variable as TokenOperator).Right = right.Variable as TokenValue;
												curs.Peek().Children.Remove(right);
												change = true;

												if(right.Variable is TokenOperator ox) {
													// check that there will be no orphan branches
													if(!ox.IsUnary && (ox as TokenOperatorBinary).Left == null)
														Program.Error(data.FileName, line, "Unexpected binary operator " + ox.OpStr + " without a right value!");
												}
											}
										}
									}
								}

								if(!change) goto nextone;
								goto rerun;
							}

							default:
								Program.Error(data.FileName, line, "Unexpected state!");
								break;
						}
						goto rerun;

					nextone:
						ignore.Add(prec);
						goto recheck;

					} else {

					}

				} else {
					// precaution
					curs.Pop();
					goto recurse;
				}
			}

			return null;
		}

		// cut part of the buffer into a string according to token
		private static string CutStr(ref char[] buf, TokenInfo t) {
			return new string(buf, t.Start, t.End - t.Start + 1);
		}

		// quick method to double the infos array size
		private static TokenInfo[] ExpandInfos(TokenInfo[] infos) {
			TokenInfo[] ret = new TokenInfo[infos.Length << 1];
			Array.Copy(infos, ret, infos.Length);
			return ret;
		}
		#endregion
	}

	#region Tokenize Classes
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

	public class TokenGroup {
		public TokenNormalEnum Type;
		public dynamic Variable;
		public int Depth;
		public List<TokenGroup> Children;

		public TokenGroup(TokenNormalEnum type, int depth, dynamic variable) {
			Type = type;
			Depth = depth;
			Variable = variable;

			Children = new List<TokenGroup>();
		}

		public void AddChild(params TokenGroup[] tokens) {
			Children.AddRange(tokens);
		}

		public int GetPrecedence() {
			return Type switch
			{
				TokenNormalEnum.Operator => (Variable as TokenOperator).Precedence,
				TokenNormalEnum.Number => 0,
				TokenNormalEnum.String => 0,
				TokenNormalEnum.Text => 0,
				TokenNormalEnum.Label => 0,
				TokenNormalEnum.MacroCall => 1,
				TokenNormalEnum.MacroArgument => -1,
				TokenNormalEnum.BlockStart => -10,
				TokenNormalEnum.BlockEnd => -10,
				TokenNormalEnum.None => -1,
				_ => -1,
			};
		}

		public bool IsValueOrOperator() => 
			Type == TokenNormalEnum.Operator || 
			Type == TokenNormalEnum.Text || 
			Type == TokenNormalEnum.String || 
			Type == TokenNormalEnum.Number || 
			Type == TokenNormalEnum.MacroCall ||
			(Type == TokenNormalEnum.Label && Variable != null);
	}

	public enum TokenNormalEnum {
		None, Operator, String, Text, Number, Label,
		MacroCall, MacroArgument,
		BlockStart, BlockEnd,
	}
	#endregion

	public class FileData {
		public List<Token> Tokens;
		public string FileName;

		public FileData(string filename) {
			FileName = filename;
			Tokens = new List<Token>();
		}
	}
}
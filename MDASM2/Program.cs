using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace MDASM2 {
	public class Program {
		#region Error Handling
		public static void Error(string error) {
			_error("Asssembly error\n-- " + error.Replace("\n", "\n-- "));
		}

		public static void Error(string filename, int line, string error) {
			_error(filename + ":" + line + "\n-- " + error.Replace("\n", "\n-- "));
		}

		private static void _error(string text) {
			Console.WriteLine(text);
			Console.ReadKey();
			Environment.Exit(0);
		}

		public static void Usage(string text) {
			throw new NotImplementedException();
		}
		#endregion

		#region Main
		public static void Main(string[] args) {
			Console.Title = "MDASM";
			// this lines makes sure Decimal points are always . instead of , for some regions!!!
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			// start the assembly timer
			Stopwatch time = new Stopwatch();
			time.Start();

			// prepare variables
			string inputdata = "";
			int ao = 0;

			// check assembler flags here
			if (args.Length > ao && args[ao].Length > 1 && args[ao][0] == '-') {
				switch (args[ao++].ToLowerInvariant()) {
					case "-e": case "-expr": {
							// create an expression to run
							if (args.Length <= ao)
								Usage("Expression flag requires an additional parameter as an expression, but no additional parameters were given!");

							inputdata += "\n"+ args[ao++];
						}
						break;

					default:
						Usage("Flag " + args[ao - 1] + " is unknown!");
						break;
				}
			}
			
			// check if we need to set input file
			if(args.Length > ao + 1)
				inputdata += "\n\tinclude\t'" + args[ao++] + "'";

			// get output file
			if (args.Length <= ao)
				Usage("Missing output file!");

			string output = args[ao++], listings = null;

			// check if we want a listings file
			if (args.Length > ao)
				listings = args[ao++];

			FileSystem.Create("", new MemoryStream(TextToBytes(inputdata)));
		//	FileSystem.Get("sonic1.asm");

			// finish assembly
			time.Stop();
			Console.WriteLine("\nAssembly completed in " + (Math.Round(time.ElapsedMilliseconds / 1000.0, 1) + "").Replace(',', '.') + " seconds! (" + time.ElapsedMilliseconds + " ms)\n");
			Console.Read();
		}
		#endregion

		#region Text
		private static byte[] TextToBytes(string inputdata) {
			return ASCIIEncoding.Default.GetBytes(inputdata);
		}

		public static dynamic StringToNum(string str, out TokenValueType type) {
			// detect prefix
			if(str.StartsWith("0x")) goto hex2;
			if(str.StartsWith("$")) goto hex1;
			if(str.StartsWith("0b")) goto bin2;

			// detect suffix
			if(str.EndsWith("h")) goto hex_1;
			if(str.EndsWith("b")) goto bin_1;

			// detect float
			if(str.Contains(".")) {
				if(!double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double fv))
					Error("", -1, "Unable to parse " + str + " as a floating point number!");

				type = TokenValueType.Float;
				return fv;
			}

			if(!long.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long dv))
				Error("", -1, "Unable to parse " + str + " as a decimal number!");

			type = TokenValueType.Int64;
			return dv;

		hex1:
			str = str.Substring(1);
			goto hexc;

		hex2:
			str = str.Substring(2);
			goto hexc;

		hex_1:
			str = str.Substring(0, str.Length - 1);
			goto hexc;

		hexc:
			if(!long.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long val))
				Error("", -1, "Unable to parse "+ str + " as a hex number!");

			type = TokenValueType.Int64;
			return val;

		bin2:
			str = str.Substring(2);
			goto binc;

		bin_1:
			str = str.Substring(0, str.Length - 1);
			goto binc;

		binc:
			try {
				type = TokenValueType.Int64;
				return Convert.ToInt64(str, 2);

			} catch(Exception) {
				Error("", -1, "Unable to parse " + str + " as a binary number!");
			}

			type = TokenValueType.None;
			return null;
		}
		#endregion
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
			string inputdata = /*"derp = 2+(2/2)"*/"";
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

		public static dynamic StringToNum(string str) {
			return str;
		}
		#endregion
	}
}

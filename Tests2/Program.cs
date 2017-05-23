using System;
using System.Collections.Generic;
using YingDev.UnsafeSerialization;
using static YingDev.UnsafeSerialization.Readers;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using YingDev.UnsafeSerialization.Utils;
using System.Linq;
using System.Threading;
using System.Runtime;

namespace Tests
{
	class ConsoleLogger : ILOGGER
	{
		public void WriteLine(string msg)
		{
			Console.WriteLine(msg);
		}
	}

	public class UnitTest1
	{
		public static void Main(string[] args)
		{

			Start(new ConsoleLogger());
			Console.Read();
		}

        public static void Test(int offset)
        {

        }

		public static unsafe void Start(ILOGGER log)
		{
			LOGGER.impl = log;

			//log.WriteLine("A");
			var defReaders = new Dictionary<Type, Delegate>
			{
				{typeof(int), new StructReader(I32Reader)},
				{typeof(bool), new StructReader(ByteReader)},
				{typeof(double), new StructReader(F64Reader)},
				{typeof(string), new ObjectReader((r, o) =>
				{
					return r.ReadCString();
				})},
			};
			LayoutInfo.Add<Point>(defReaders);
			LayoutInfo.Add<Inner>(defReaders);
            LayoutInfo.Add<MyStruct>(defReaders);

            LayoutInfo.Add<X>(defReaders);

			var src = new X
			{
				serverId = 888,
				a = 123,
				str = "hello world",
				value = 789,
				pts = new[] { new Point { x = 1990, y = 2 }, new Point { x = 3, y = 4 }, new Point { x = 5, y = 6 } }
			};

			using (var stream = new MemoryStream(1280))
			{
				using (var w = new BinaryWriter(stream, Encoding.ASCII, true))
				{
					w.Write(src.serverId);
					w.Write(src.a);

					//pt
					w.Write(66);
					w.Write(88);

					for (var i = 0; i < src.str.Length; i++)
						w.Write((byte)src.str[i]);
					w.Write((byte)0);

					w.Write(src.value);
					w.Write(true);
					w.Write(123.0);
					// w.Write(DateTime.Now.Ticks);
					w.Write((byte)src.pts.Length);
					for (var i = 0; i < src.pts.Length; i++)
					{
						var pt = src.pts[i];
						w.Write(pt.x);
						w.Write(pt.y);
					}

                    //inner struct...
                    w.Write(999);
					var str2 = "inner Name!";
					for (var i = 0; i < str2.Length; i++)
						w.Write(str2[i]);
					w.Write((byte)0);

					w.Write(620);
				}
				//using (var r = new BinaryReader(stream))
				{
					var data = stream.GetBuffer();
					fixed (byte* dataPtr = &data[0])
					{
						var fastBuf = new UnsafeBinaryReader { p = dataPtr };
						var result = (X)MessageReader(fastBuf, typeof(X));
						// GC.Collect();
						log.WriteLine(result.ToString());



						unsafe
						{
							var t = typeof(X);

							var fa = t.GetField("a");
							var ff64 = t.GetField("f64");
							var finner = t.GetField("inner");
							var fstr = t.GetField("str");
							var fnumMap = t.GetField("numMap");
							var fhaha = t.GetField("haha");
							var falert = t.GetField("alert");
							var fisOk = t.GetField("isOk");
							var fpts = t.GetField("pts");
							var fpt = t.GetField("pt");
							var fserverId = t.GetField("serverId");
							//var ftime = t.GetField("time");
							var fvalue = t.GetField("value");
							var fpt2 = t.GetField("pt2");

							var N = 1000 * 1000;

							//GC.RegisterForFullGCNotification(10, 10);
							/*new Thread(s => {
								while(true)
								{
                                    Thread.Sleep(100);
                                    Console.Write("*");
                                    GC.Collect(2, GCCollectionMode.Forced, true, true);
								}

							}).Start();*/
							//Console.WriteLine(GC.TryStartNoGCRegion(N * 10));
							//var arr = new X[N];
							var sw = Stopwatch.StartNew();
							for (var i = 0; i < N; i++)
							{
								fastBuf._rPos = 0;
								var x = (X)Activator.CreateInstance(typeof(X));

								x.a = i;
								x.f64 = 123.0;
                                x.inner = new MyStruct { A = 5, inner = new Inner() };
								x.str = new string(new char[] { 'h', 'l', 'l', 'o', 'w', 'o', 'l', 'd', 'x' });
								x.haha = "";
								x.alert = null;
								x.numMap = new Dictionary<int, int> { { 112, 222 } };
								x.isOk = i % 2 == 0;
								x.pts = new Point[]
									{new Point {x = 1, y = 2}, new Point {x = 3, y = 4}, new Point {x = 5, y = 6}};
								x.pt = new Point { x = 1, y = 1990 };
								x.serverId = 888;
								//x.time = new ValueBox<DateTime> {Value = DateTime.Now};
								x.value = i;
								x.pt2 = 222;

								//arr[i] = x;

							}
							sw.Stop();
							log.WriteLine("DIRECT = " + sw.ElapsedMilliseconds + " ms");
							//arr = new X[N];
							GC.Collect(2, GCCollectionMode.Forced, true);
							GC.WaitForPendingFinalizers();
							Thread.Sleep(1000);

							sw.Reset();
							sw.Start();
							for (var i = 0; i < N; i++)
							{
								//Thread.Sleep(2);
								fastBuf._rPos = 0;
								MessageReader(fastBuf, t);

							}
							sw.Stop();
							log.WriteLine("READER = " + sw.ElapsedMilliseconds + " ms");
							//arr = new X[N];
							GC.Collect(2, GCCollectionMode.Forced, true);
							GC.WaitForPendingFinalizers();
							Thread.Sleep(1000);

							sw.Reset();
							sw.Start();
							for (var i = 0; i < N; i++)
							{
								fastBuf._rPos = 0;
								var x = (X)Activator.CreateInstance(typeof(X));

								fa.SetValue(x, fastBuf.ReadByte());
								ff64.SetValue(x, fastBuf.ReadByte());
								finner.SetValue(x, new MyStruct { A = 1, inner = new Inner()});
								fstr.SetValue(x, new string(new char[] { 'h', 'l', 'l', 'o', 'w', 'o', 'l', 'd', 'x' }));
								fnumMap.SetValue(x, new Dictionary<int, int> { { 112, 222 } });
								fisOk.SetValue(x, Convert.ToBoolean(fastBuf.ReadByte()));
								falert.SetValue(x, null);
								fhaha.SetValue(x, "haha");
								fpts.SetValue(x,
									new Point[]
									{
									new Point {x = fastBuf.ReadByte(), y = fastBuf.ReadByte()},
									new Point {x = fastBuf.ReadByte(), y = fastBuf.ReadByte()},
									new Point {x = fastBuf.ReadByte(), y = fastBuf.ReadByte()}
									});
								fpt.SetValue(x, new Point { x = fastBuf.ReadByte(), y = fastBuf.ReadByte() });
								fserverId.SetValue(x, fastBuf.ReadByte());
								//ftime.SetValue(x, new ValueBox<DateTime> {Value = new DateTime((long) fastBuf.ReadByte())});
								fvalue.SetValue(x, fastBuf.ReadByte());
								fpt2.SetValue(x, fastBuf.ReadByte());

								//arr[i] = x;

							}
							sw.Stop();
							log.WriteLine("REFLECT = " + sw.ElapsedMilliseconds + " ms");
							//var file = File.CreateText("log.txt");*/


							Console.Read();
							for (var i = 0; i < 5; i++)
							{
								//Console.WriteLine(arr[i]);
							}
							Console.WriteLine("======");
							Console.Read();
						}
					}

				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		public class BaseX
		{
			public int serverId;

			public object alert;
			static ObjectReader alertReader = (r, o) => null;
		}

		[StructLayout(LayoutKind.Sequential)]
		public class ValueBox<T> where T : struct
		{
			public T Value;

			public override string ToString() => $"ValueBox[ {Value.ToString()} ]";
		}

		[UnsafeSerialize, StructLayout(LayoutKind.Sequential)]
		public class X : BaseX
		{


			//int _haha;
			//public int Haha { get { return _haha; } }

			object hoho;
			public int a;
			public Point pt;
			public string str;
			public int value;

			public bool isOk;
			public double f64;

			/*[MarshalAs(Interface)] public ValueBox<DateTime> time;
            static ObjectReader timeReader = (r, o) =>
            {
                unsafe
                {
                    var dum = 0L;
                    r.Read8BytesTo((byte*)&dum);
                    return new ValueBox<DateTime> {Value = new DateTime(dum)};
                }
            };*/

			public Point[] pts;
			//static ObjectReader ptsReader = ValueArrayReader<Point>((r, o) => new Point[r.ReadByte()], LayoutReader<Point>());
			static ObjectReader ptsReader = BlittableValueArrayReader<Point>((r, o) => new Point[r.ReadByte()]);

			public object numMap;
			static ObjectReader numMapReader = (r, o) =>
			{
				return new Dictionary<int, int> { { 112, 222 } };
			};
			public string haha;
			static ObjectReader hahaReader = CondReader<X>(o => true, (r, o) => "shit");

            //public Inner inner;
            public MyStruct inner;

			static StructReader pt2Reader = CondReader<X>(o =>
			{
				return true;
			}, I32Reader);
			public int pt2;

			public override string ToString() => this.ToStringUsingLayoutInfo();

			/*~X()
            {
                Console.WriteLine("~X");
            }*/
		}

        [UnsafeSerialize]
        public struct MyStruct
        {
            public int A;
            public Inner inner;

            public string strr;
            static ObjectReader strrReader = (r, o) => "strrrrrrrr";

            public override string ToString()
            {
                return this.ToStringUsingLayoutInfo();
            }
        }

		[UnsafeSerialize, StructLayout(LayoutKind.Sequential)]
		public class Inner
		{
			public string name;
			double _dum1;
			double _dum2;

			public void Test()
			{
				Console.WriteLine("Inner.Test: " + name);
			}

            public override string ToString()
            {
                return "Inner: " + name;
            }

            /*~Inner()
            {
                Console.Write("~Inner");
            }*/
        }

		[StructLayout(LayoutKind.Sequential)]
		public struct Point
		{
			public int x;
			public int y;

			public override string ToString()
			{
				return string.Format("Point({0}, {1})", x, y);
			}
		}
	}
}

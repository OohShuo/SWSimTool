using System;
public static class CoreTestRunner {
 public static int Main() {
  try { CollisionMathTests.Main(); TestRootJointSerialization.Main(); return 0; }
  catch(Exception error) { Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message + "\n" + error.StackTrace); return 1; }
 }
}

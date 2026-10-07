namespace SWSimTool.Simulation {
 public static class ExportInstrumentation {
  public static int GeometryQueries {get;private set;}
  public static int StlExports {get;private set;}
  public static void GeometryQuery(){GeometryQueries++;}
  public static void StlExport(){StlExports++;}
 }
}

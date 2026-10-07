using System;namespace SWSimTool.Simulation {
 [Flags] public enum SimulationDirtyFlags {None=0,Source=1,Mesh=2,Site=4,Collision=8,Joint=16,Equality=32,Actuator=64,Sensor=128,Solver=256,SiteForce=512,Mjcf=1024}
}

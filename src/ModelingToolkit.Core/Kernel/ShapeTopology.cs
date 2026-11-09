namespace ModelingToolkit.Core.Kernel;

/// <summary>How many solids, faces, edges and vertices a shape has.</summary>
public readonly record struct ShapeTopology(int Solids, int Faces, int Edges, int Vertices);

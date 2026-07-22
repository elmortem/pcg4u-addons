namespace PCG.Sweep
{
	internal enum ManifoldError
	{
		NoError = 0,
		NonFiniteVertex = 1,
		NotManifold = 2,
		VertexIndexOutOfBounds = 3,
		PropertiesWrongLength = 4,
		MissingPositionProperties = 5,
		MergeVectorsDifferentLengths = 6,
		MergeIndexOutOfBounds = 7,
		TransformWrongLength = 8,
		RunIndexWrongLength = 9,
		FaceIdWrongLength = 10,
		InvalidConstruction = 11,
		ResultTooLarge = 12,
		InvalidTangents = 13,
		Cancelled = 14
	}
}

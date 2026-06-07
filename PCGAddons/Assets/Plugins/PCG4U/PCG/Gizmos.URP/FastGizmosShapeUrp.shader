Shader "PCG4U/FastGizmosShapeUrp"
{
	Properties
	{
		_LightDirection ("Light Direction", Vector) = (0.3, -0.7, 0.3, 0)
		_ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.3
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
		Pass
		{
			Name "FastGizmos"
			ZWrite On
			ZTest LEqual
			Cull Back

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			float4x4 unity_MatrixVP;

			StructuredBuffer<float4x4> _Matrices;
			StructuredBuffer<float4> _Colors;
			float4 _LightDirection;
			float _ShadowStrength;

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				uint instanceID : SV_InstanceID;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 normalWS : TEXCOORD0;
				float3 color : TEXCOORD1;
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				float4x4 objectToWorld = _Matrices[input.instanceID];
				float3 positionWS = mul(objectToWorld, float4(input.positionOS.xyz, 1.0)).xyz;
				output.positionCS = mul(unity_MatrixVP, float4(positionWS, 1.0));
				output.normalWS = normalize(mul((float3x3)objectToWorld, input.normalOS));
				output.color = _Colors[input.instanceID].rgb;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				float ndotl = max(0.0, dot(normalize(input.normalWS), normalize(-_LightDirection.xyz)));
				float lighting = lerp(1.0 - _ShadowStrength, 1.0, ndotl);
				return half4(input.color * lighting, 1.0);
			}
			ENDHLSL
		}
	}
}

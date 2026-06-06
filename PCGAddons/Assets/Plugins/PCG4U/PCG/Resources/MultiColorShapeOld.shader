Shader "Custom/MultiColorShapeOld"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        CGPROGRAM
        #pragma surface surf Lambert
        #pragma instancing_options procedural:setup

        struct Input
        {
            float2 uv_MainTex;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        StructuredBuffer<float4x4> _Matrices;
        StructuredBuffer<float4> _Colors;
        #endif

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            unity_ObjectToWorld = _Matrices[unity_InstanceID];
            #endif
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            o.Albedo = _Colors[unity_InstanceID].rgb;
            #endif

            o.Albedo = float3(1, 0, 0); // Red color for debugging
        }
        ENDCG
    }
    FallBack "Diffuse"
}
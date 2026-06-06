Shader "Custom/MultiColorShape"
{
    Properties
    {
        _LightDirection ("Light Direction", Vector) = (0.3, -0.7, 0.3, 0)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        LOD 100

        CGPROGRAM
        #pragma surface surf CustomLighting vertex:vert addshadow
        #pragma instancing_options procedural:setup
        #pragma target 3.0

        float4 _LightDirection;
        float _ShadowStrength;

        struct Input
        {
            float3 customColor;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        StructuredBuffer<float4x4> _Matrices;
        StructuredBuffer<float4> _Colors;
        #endif

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            unity_ObjectToWorld = _Matrices[unity_InstanceID];
            unity_WorldToObject = unity_ObjectToWorld;
            unity_WorldToObject._14_24_34 *= -1;
            unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
            #endif
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(Input, o);

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            o.customColor = _Colors[unity_InstanceID].rgb;
            #else
            o.customColor = float3(1, 0, 0); // Default red color
            #endif
        }

        float4 LightingCustomLighting(SurfaceOutput s, float3 lightDir, float atten)
        {
            float3 normal = s.Normal;
            float ndotl = max(0, dot(normal, normalize(-_LightDirection.xyz)));
            float lighting = lerp(1 - _ShadowStrength, 1, ndotl);
            
            float4 final;
            final.rgb = s.Albedo * lighting;
            final.a = s.Alpha;
            return final;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo = IN.customColor;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
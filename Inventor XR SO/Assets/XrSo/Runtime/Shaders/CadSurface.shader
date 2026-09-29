Shader "XrSo/CadSurface"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.72,0.74,0.77,1)
        _UseVertexColor ("Use vertex colour", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _UseVertexColor;
            CBUFFER_END
            float4 _XrSectionPlane;
            float _XrSectionEnabled;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                return o;
            }
            half4 frag(Varyings input, FRONT_FACE_TYPE front : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_XrSectionEnabled > 0.5) clip(-dot(float4(input.positionWS,1), _XrSectionPlane));
                half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(front, 1, -1);
                Light light = GetMainLight();
                half3 lighting = half3(0.3,0.3,0.3) + light.color * saturate(dot(normal, light.direction)) * 0.7;
                half3 albedo = _UseVertexColor > 0.5 ? input.color.rgb : _BaseColor.rgb;
                return half4(albedo * lighting, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}

Shader "XrSo/HighlightOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.6, 0.1, 0.6)
        // Selection rim (inverted hull): Cull Front + _Expand > 0 draws the mesh's back faces pushed out along the normal,
        // so only a halo around the silhouette shows. _Expand is an angular width (metres of offset per metre of camera
        // distance), so the rim keeps the same apparent thickness near and far. Defaults (Cull Back, 0) leave every other user unchanged.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Expand ("Rim width (m per m of distance)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _XrSectionPlane;
            float _XrSectionEnabled;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Expand;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                if (_Expand > 0)
                {
                    float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                    float camDistance = clamp(length(_WorldSpaceCameraPos - positionWS), 0.4, 4.0);
                    positionWS += normalWS * (_Expand * camDistance);
                }
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_XrSectionEnabled > 0.5) clip(-dot(float4(input.positionWS,1), _XrSectionPlane));
                return _Color;
            }
            ENDHLSL
        }
    }
}

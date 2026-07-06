Shader "Skybox/Panoramic Blend"
{
    Properties
    {
        _TexA ("Panoramic A (Bright)", 2D) = "grey" {}
        _TexB ("Panoramic B (Dark)", 2D) = "grey" {}
        _Blend ("Blend", Range(0, 1)) = 0
        _Exposure ("Exposure", Float) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [Toggle] _MirrorOnBack ("Mirror On Back", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexA;
            sampler2D _TexB;
            float _Blend;
            float _Exposure;
            float _Rotation;
            float _MirrorOnBack;

            float3 RotateAroundY(float3 v, float deg)
            {
                float rad = deg * UNITY_PI / 180.0;
                float s = sin(rad);
                float c = cos(rad);
                return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
            }

            float2 DirToEquirectUV(float3 dir)
            {
                // 与 Unity 内置 Skybox/Panoramic 一致：v = asin(y)/PI + 0.5
                dir = normalize(dir);
                float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * UNITY_PI);
                float v = 0.5 + asin(clamp(dir.y, -1.0, 1.0)) / UNITY_PI;
                return float2(u, v);
            }

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = RotateAroundY(v.vertex.xyz, _Rotation);
                if (_MirrorOnBack > 0.5 && v.vertex.z > 0)
                    o.pos.x = -o.pos.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = DirToEquirectUV(i.dir);
                fixed4 colA = tex2D(_TexA, uv);
                fixed4 colB = tex2D(_TexB, uv);
                fixed4 col = lerp(colA, colB, _Blend);
                col.rgb *= _Exposure;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}

Shader "Hidden/MeshHeightBakeWorldY"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma enable_d3d11_debug_symbols
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float worldY : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldY = world.y;

                return o;
            }

            float frag(v2f i) : SV_Target
            {
                return i.worldY;
            }
            ENDHLSL
        }
    }
}
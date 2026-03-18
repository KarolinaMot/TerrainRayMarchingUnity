
Shader "Unlit/Raymarcher"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #define MAX_STEPS 100
            #define MAX_DISTANCE 100
            #define SURFACE_DIST 0.001f
            
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 rayOrigin : TEXCOORD1;
                float3 hitPos : TEXCOORD2;

            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.rayOrigin = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.f));
                o.hitPos =  v.vertex;

                return o;
            }

            float GetDistance(float3 p)
            {
                float d = length(p) - 0.5f;
                d= length(float2(length(p.xy)-0.5, p.z)) - .1f;
                return d;
            }

            float3 GetNormal(float3 p)
            {
                float2 offset=float2(1e-2, 0);
                float3 n = GetDistance(p) - float3(GetDistance(p-offset.xyy),GetDistance(p-offset.yxy), GetDistance(p-offset.yyx));
                return normalize(n);
            }

            float Raymarch(float3 rOrigin, float3 rDirection)
            {
                float distanceFromOrigin = 0;
                float distanceFromSurface = 0;

                for(int i=0; i<MAX_STEPS; i++)
                {
                    float3 p = rOrigin + distanceFromOrigin * rDirection;
                    distanceFromSurface = GetDistance(p);

                    distanceFromOrigin +=distanceFromSurface;

                    if(distanceFromSurface<SURFACE_DIST || distanceFromOrigin > MAX_DISTANCE)
                    break;
                }

                return distanceFromOrigin;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //Camera stuff
                //Substracing 0.5f from the uv, so the uv origin is in the middle of the face
                float2 uv = i.uv - .5f;

                float3 rayOrigin =  i.rayOrigin;
                float3 rayDirection = normalize(i.hitPos-rayOrigin);

                // // sample the texture
                float d = Raymarch(rayOrigin, rayDirection);
                fixed4 col = 0;

                if(d<MAX_DISTANCE)
                {
                    float3 p = rayOrigin + rayDirection * d;
                    float3 n = GetNormal(p);

                    col.rgb = n;
                }
                else
                {
                    discard;
                }

                return col;
            }
            ENDCG
        }
    }
}

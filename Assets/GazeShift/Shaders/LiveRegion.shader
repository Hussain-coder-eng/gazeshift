Shader "GazeShift/LiveRegion"
{
    Properties
    {
        _MainTex ("Live camera", 2D) = "black" {}
        _Blur ("Blur", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Blur;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 step = _MainTex_TexelSize.xy * 5.0;
                fixed3 sharp = tex2D(_MainTex, input.uv).rgb;
                if (_Blur < 0.5) return fixed4(sharp, 1);
                fixed3 sum = sharp * 4;
                sum += tex2D(_MainTex, saturate(input.uv + float2(step.x, 0))).rgb * 2;
                sum += tex2D(_MainTex, saturate(input.uv - float2(step.x, 0))).rgb * 2;
                sum += tex2D(_MainTex, saturate(input.uv + float2(0, step.y))).rgb * 2;
                sum += tex2D(_MainTex, saturate(input.uv - float2(0, step.y))).rgb * 2;
                sum += tex2D(_MainTex, saturate(input.uv + step)).rgb;
                sum += tex2D(_MainTex, saturate(input.uv - step)).rgb;
                sum += tex2D(_MainTex, saturate(input.uv + float2(step.x, -step.y))).rgb;
                sum += tex2D(_MainTex, saturate(input.uv + float2(-step.x, step.y))).rgb;
                return fixed4(sum / 16, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}

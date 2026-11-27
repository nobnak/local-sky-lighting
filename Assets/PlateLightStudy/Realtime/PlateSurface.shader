Shader "Local Sky Lighting/Struck Plate" {
    Properties {
        [NoScaleOffset] _Surface ("Strike slopes XY and polish", 2D) = "gray" {}
        _SlopeStrength ("Strike normal strength", Float) = 1
        _RoughnessScale ("Roughness scale", Range(0.5, 2)) = 1
        _Anisotropy ("Anisotropy", Range(0, 0.75)) = 0.34
        _Exposure ("Exposure multiplier", Range(0.05, 2)) = 0.55
        [Toggle] _Rim ("Rim material", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            #define MAX_LIGHTS 9
            #define PI 3.14159265

            sampler2D _Surface;
            float _SlopeStrength, _RoughnessScale, _Anisotropy, _Exposure, _Rim;
            int _LightCount;
            float4 _LightPosition[MAX_LIGHTS];
            float4 _LightRight[MAX_LIGHTS];
            float4 _LightUp[MAX_LIGHTS];
            float4 _LightForward[MAX_LIGHTS];
            float4 _LightRadiance[MAX_LIGHTS];

            struct Attributes {
                float4 position : POSITION;
                float3 normal : NORMAL;
            };

            struct Varyings {
                float4 clip : SV_POSITION;
                float3 position : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                output.clip = UnityObjectToClipPos(input.position);
                output.position = input.position.xyz;
                output.normal = input.normal;
                return output;
            }

            float2 SlopeCdf(float2 x) { return 0.5 + 0.5 * x * rsqrt(1 + x * x); }

            // Integrate the two marginal GGX slope distributions over the light rectangle.
            // The separable footprint approximates the projected area integral.
            float RectangleSpecular(int i, float3 p, float3 reflection, float2 alpha) {
                float3 forward = _LightForward[i].xyz;
                float incidence = -dot(reflection, forward);
                float distance = dot(p - _LightPosition[i].xyz, forward) / max(incidence, 0.0001);
                if (incidence <= 0 || distance <= 0) return 0;

                float3 hit = p + reflection * distance - _LightPosition[i].xyz;
                float2 dimensions = float2(_LightRight[i].w, _LightUp[i].w);
                float2 center = float2(dot(hit, _LightRight[i].xyz), dot(hit, _LightUp[i].xyz));
                float2 sigma = max(0.003, 2 * distance * alpha / sqrt(max(incidence, 0.15)));
                float2 area = SlopeCdf((dimensions - center) / sigma) - SlopeCdf((-dimensions - center) / sigma);
                return area.x * area.y;
            }

            float2 RoughnessAxes(float roughness, float anisotropy) {
                float aspect = sqrt(1 - 0.9 * anisotropy);
                return max(0.002, roughness * roughness * float2(1 / aspect, aspect));
            }

            float3 ToneMap(float3 value) {
                float luma = dot(value, float3(0.2126, 0.7152, 0.0722));
                float shaped = pow(luma, 1.3);
                float base = shaped / (shaped + 1.45);
                float3 tint = value / max(luma, 0.00001);
                return saturate(base * lerp(tint, 1, smoothstep(0.15, 0.9, base)));
            }

            float4 Frag(Varyings input) : SV_Target {
                float3 p = input.position;
                float3 neutral = normalize(input.normal);
                const float3 view = float3(0, 0, -1);
                float3 surface = tex2D(_Surface, p.xy / 3.24 + 0.5).rgb;
                float3 slope = float3((surface.rg - 0.5) * _SlopeStrength, 0);
                float3 strikeNormal = normalize(neutral + slope * (1 - _Rim));
                float3 bodyNormal = normalize(neutral + 0.38 * slope * (1 - _Rim));
                float3 strikeRay = reflect(-view, strikeNormal);
                float3 bodyRay = reflect(-view, bodyNormal);
                float edge = smoothstep(0.39, 1.46, length(p));
                float3 bodyColor = lerp(float3(0.37, 0.104, 0.035), float3(0.071, 0.012, 0.015), edge);
                bodyColor = lerp(bodyColor, float3(0.062, 0.020, 0.018), _Rim);
                float3 alloy = lerp(float3(0.79, 0.53, 0.20), float3(0.19, 0.046, 0.031), edge);
                float polished = lerp(0.01, 0.78, surface.b) * (1 - _Rim);
                float roughness = lerp(0.42, 0.075, surface.b) * _RoughnessScale;
                float2 strikeAlpha = RoughnessAxes(roughness, _Anisotropy);
                float2 bodyAlpha = RoughnessAxes(lerp(0.29, 0.19, _Rim) * _RoughnessScale, 0);
                float fresnel = pow(1 - saturate(dot(bodyNormal, view)), 5);
                float3 bodyF0 = lerp(0.018, bodyColor, 0.06);
                float3 bodyF = lerp(bodyF0, 1, fresnel);
                float3 strikeF = lerp(alloy, 1, pow(1 - saturate(dot(strikeNormal, view)), 5));
                float3 radiance = bodyColor * 0.012;

                [loop] for (int i = 0; i < _LightCount; i++) {
                    float3 toward = _LightPosition[i].xyz - p;
                    float distanceSq = max(dot(toward, toward), 0.0001);
                    float3 lightDir = toward * rsqrt(distanceSq);
                    float emitterCos = saturate(-dot(lightDir, _LightForward[i].xyz));
                    float3 irradiance = _LightRadiance[i].rgb * _LightRadiance[i].w * emitterCos / distanceSq;
                    float3 diffuse = irradiance * saturate(dot(bodyNormal, lightDir)) * bodyColor * (1 - bodyF) / PI;
                    float3 body = bodyF * _LightRadiance[i].rgb * RectangleSpecular(i, p, bodyRay, bodyAlpha);
                    float3 metal = strikeF * _LightRadiance[i].rgb * RectangleSpecular(i, p, strikeRay, strikeAlpha);
                    radiance += lerp(diffuse + body, metal, polished);
                }
                return float4(ToneMap(radiance * _Exposure), 1);
            }
            ENDHLSL
        }
    }
}

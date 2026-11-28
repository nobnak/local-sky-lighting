Shader "Local Sky Lighting/Struck Plate" {
    Properties {
        [NoScaleOffset] _Surface ("Strike slopes XY and polish", 2D) = "gray" {}
        [NoScaleOffset] _ScratchDirection ("Scratch direction", 2D) = "gray" {}
        _MirrorShare ("Mirror reflection weight", Range(0, 1)) = 0.74
        _SlopeStrength ("Strike normal strength", Float) = 1
        _RoughnessScale ("Roughness scale", Range(0.5, 2)) = 1
        _Anisotropy ("Anisotropy", Range(0, 0.9)) = 0.7
        _Exposure ("Exposure multiplier", Range(0.05, 2)) = 0.55
        [Toggle] _ContinuousMetal ("Continuous metal surface", Float) = 0
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

            sampler2D _Surface, _ScratchDirection;
            float _SlopeStrength, _RoughnessScale, _Anisotropy, _Exposure, _Rim, _MirrorShare;
            float _ContinuousMetal;
            int _LightCount;
            float4 _Projection;
            float _ProjectionFalloffScale;
            float4 _LightPosition[MAX_LIGHTS];
            float4 _LightRight[MAX_LIGHTS];
            float4 _LightUp[MAX_LIGHTS];
            float4 _LightForward[MAX_LIGHTS];
            float4 _LightRadiance[MAX_LIGHTS];
            float4 _BeamCones[MAX_LIGHTS];

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

            float2 ProjectScratchLobe(int i, float3 grain, float2 axes) {
                float2 aligned = float2(dot(grain, _LightRight[i].xyz), dot(grain, _LightUp[i].xyz));
                float2 weight = saturate(aligned * aligned);
                return sqrt(lerp(axes.y * axes.y, axes.x * axes.x, weight));
            }

            float4 Frag(Varyings input) : SV_Target {
                float3 p = input.position;
                float3 neutral = normalize(input.normal);
                float3 view = unity_OrthoParams.w > 0.5
                    ? normalize(mul((float3x3)unity_WorldToObject, UNITY_MATRIX_V[2].xyz))
                    : normalize(ObjSpaceViewDir(float4(p, 1)));
                float2 uv = p.xy / 3.24 + 0.5;
                float3 surface = tex2D(_Surface, uv).rgb;
                float2 grainVector = tex2D(_ScratchDirection, uv).rg * 2 - 1;
                float confidence = saturate(length(grainVector));
                float grainAngle = 0.5 * atan2(grainVector.y, grainVector.x + 0.00001);
                float3 grain = float3(cos(grainAngle), sin(grainAngle), 0);
                float3 slope = float3((surface.rg - 0.5) * _SlopeStrength, 0);
                float3 strikeNormal = normalize(neutral + slope * (1 - _Rim));
                float3 bodyNormal = normalize(neutral + 0.38 * slope * (1 - _Rim));
                float3 strikeRay = reflect(-view, strikeNormal);
                float3 bodyRay = reflect(-view, bodyNormal);
                float edge = smoothstep(0.39, 1.46, length(p));
                float3 bodyColor = lerp(float3(0.37, 0.104, 0.035), float3(0.071, 0.012, 0.015), edge);
                bodyColor = lerp(bodyColor, float3(0.062, 0.020, 0.018), _Rim);
                float3 alloy = lerp(float3(0.79, 0.53, 0.20), float3(0.19, 0.046, 0.031), edge);
                float polished = lerp(lerp(0.01, 0.89, surface.b), 1, _ContinuousMetal) * (1 - _Rim);
                float mirrorRoughness = lerp(0.24, 0.033, surface.b) * _RoughnessScale;
                float2 mirrorAlpha = RoughnessAxes(mirrorRoughness, 0);
                float2 scratchAlpha = RoughnessAxes(lerp(0.31, 0.105, surface.b) * _RoughnessScale,
                    _Anisotropy * confidence);
                float2 bodyAlpha = RoughnessAxes(lerp(0.29, 0.19, _Rim) * _RoughnessScale, 0);
                float fresnel = pow(1 - saturate(dot(bodyNormal, view)), 5);
                float3 bodyF0 = lerp(0.018, bodyColor, 0.06);
                float3 bodyF = lerp(bodyF0, 1, fresnel);
                float3 strikeF = lerp(alloy, 1, pow(1 - saturate(dot(strikeNormal, view)), 5));
                float radius = length(p.xy - _Projection.xy);
                float falloffScale = max(1, _ProjectionFalloffScale);
                float mask = 1 - smoothstep(_Projection.z * 0.73, _Projection.z * falloffScale, radius);
                // Preserve a weak tail while keeping the pinch presets distinct.
                mask *= lerp(1, mask, saturate(falloffScale - 1));
                float3 background = bodyColor * 0.022;
                background += _Rim * lerp(float3(0.002, 0.0008, 0.0004), float3(0.017, 0.008, 0.003), fresnel);
                if (mask <= 0) return float4(background * _Exposure, 1);
                float fadeCenter = lerp(0.58 + 0.42 * smoothstep(0.32, 1.25, radius), 1, _Projection.w);
                float gain = _Projection.w * _Projection.w;
                float3 radiance = 0;

                [loop] for (int i = 0; i < _LightCount; i++) {
                    float3 toward = _LightPosition[i].xyz - p;
                    float distanceSq = max(dot(toward, toward), 0.0001);
                    float3 lightDir = toward * rsqrt(distanceSq);
                    float emitterCos = saturate(-dot(lightDir, _LightForward[i].xyz));
                    float beam = smoothstep(_BeamCones[i].x, _BeamCones[i].y, emitterCos);
                    if (beam <= 0) continue;
                    float3 irradiance = _LightRadiance[i].rgb * _LightRadiance[i].w * emitterCos / distanceSq;
                    float3 diffuse = irradiance * saturate(dot(bodyNormal, lightDir)) * bodyColor * (1 - bodyF) / PI;
                    float3 body = bodyF * _LightRadiance[i].rgb * RectangleSpecular(i, p, bodyRay, bodyAlpha);
                    float mirror = RectangleSpecular(i, p, strikeRay, mirrorAlpha);
                    float scratch = RectangleSpecular(i, p, strikeRay, ProjectScratchLobe(i, grain, scratchAlpha));
                    // Fade the broad field faster than the small highlights in the lighting study.
                    float compact = 1 - smoothstep(0.04, 0.64, _LightRadiance[i].w);
                    float specularGain = lerp(gain, _Projection.w, compact);
                    float3 metal = strikeF * _LightRadiance[i].rgb * lerp(scratch, mirror, _MirrorShare) * specularGain;
                    radiance += beam * lerp(gain * (diffuse + body), metal, polished);
                }
                return float4((background + mask * fadeCenter * radiance) * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}

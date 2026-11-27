Shader "Local Sky Lighting/Struck Plate" {
    Properties {
        [NoScaleOffset] _Surface ("Strike slopes XY and polish", 2D) = "gray" {}
        [NoScaleOffset] _ScratchDirection ("Scratch direction", 2D) = "gray" {}
        _MetalColor ("Gold metal color", Color) = (0.965, 0.835, 0.53, 1)
        _MirrorShare ("Mirror reflection weight", Range(0, 1)) = 0.74
        _SlopeStrength ("Strike normal strength", Float) = 1
        _RoughnessScale ("Roughness scale", Range(0.5, 2)) = 1
        _Anisotropy ("Anisotropy", Range(0, 0.9)) = 0.7
        _EnvironmentStrength ("Environment strength", Range(0, 0.5)) = 0.18
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

            #define PI 3.14159265

            sampler2D _Surface, _ScratchDirection;
            float _SlopeStrength, _RoughnessScale, _Anisotropy, _Exposure, _Rim, _MirrorShare, _EnvironmentStrength;
            float4 _MetalColor;
            float4 _LightPosition, _LightRight, _LightUp, _LightForward, _LightRadiance, _BeamCone;

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
            float RectangleSpecular(float3 p, float3 reflection, float2 alpha) {
                float3 forward = _LightForward.xyz;
                float incidence = -dot(reflection, forward);
                float distance = dot(p - _LightPosition.xyz, forward) / max(incidence, 0.0001);
                if (incidence <= 0 || distance <= 0) return 0;

                float3 hit = p + reflection * distance - _LightPosition.xyz;
                float2 dimensions = float2(_LightRight.w, _LightUp.w);
                float2 center = float2(dot(hit, _LightRight.xyz), dot(hit, _LightUp.xyz));
                float2 sigma = max(0.003, 2 * distance * alpha / sqrt(max(incidence, 0.15)));
                float2 area = SlopeCdf((dimensions - center) / sigma) - SlopeCdf((-dimensions - center) / sigma);
                return area.x * area.y;
            }

            float2 RoughnessAxes(float roughness, float anisotropy) {
                float aspect = sqrt(1 - 0.9 * anisotropy);
                return max(0.002, roughness * roughness * float2(1 / aspect, aspect));
            }

            float2 ProjectScratchLobe(float3 grain, float2 axes) {
                float2 aligned = float2(dot(grain, _LightRight.xyz), dot(grain, _LightUp.xyz));
                float2 weight = saturate(aligned * aligned);
                return sqrt(lerp(axes.y * axes.y, axes.x * axes.x, weight));
            }

            float4 Frag(Varyings input) : SV_Target {
                float3 p = input.position;
                float3 neutral = normalize(input.normal);
                const float3 view = float3(0, 0, -1);
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
                float3 alloy = _MetalColor.rgb * lerp(1, float3(0.48, 0.28, 0.25), edge);
                float polished = lerp(0.01, 0.89, surface.b) * (1 - _Rim);
                float mirrorRoughness = lerp(0.24, 0.033, surface.b) * _RoughnessScale;
                float2 mirrorAlpha = RoughnessAxes(mirrorRoughness, 0);
                float2 scratchAlpha = RoughnessAxes(lerp(0.31, 0.105, surface.b) * _RoughnessScale,
                    _Anisotropy * confidence);
                float2 bodyAlpha = RoughnessAxes(lerp(0.29, 0.19, _Rim) * _RoughnessScale, 0);
                float fresnel = pow(1 - saturate(dot(bodyNormal, view)), 5);
                float3 bodyF0 = lerp(0.018, bodyColor, 0.06);
                float3 bodyF = lerp(bodyF0, 1, fresnel);
                float3 strikeF = lerp(alloy, 1, pow(1 - saturate(dot(strikeNormal, view)), 5));
                // A dim, broad neutral environment reveals the form outside the spot.
                const float3 environmentDirection = float3(-0.26, 0.45, -0.854);
                float diffuseEnvironment = lerp(0.22, 0.72, saturate(dot(bodyNormal, environmentDirection)));
                float bodyEnvironment = pow(saturate(dot(bodyRay, environmentDirection)), 3);
                float strikeEnvironment = pow(saturate(dot(strikeRay, environmentDirection)), 3);
                float3 environment = _EnvironmentStrength * lerp(
                    bodyColor * diffuseEnvironment + bodyF * bodyEnvironment * 0.35,
                    strikeF * lerp(0.03, 0.24, strikeEnvironment), polished);

                float3 toward = _LightPosition.xyz - p;
                float distanceSq = max(dot(toward, toward), 0.0001);
                float3 lightDir = toward * rsqrt(distanceSq);
                float emitterCos = saturate(-dot(lightDir, _LightForward.xyz));
                float beam = smoothstep(_BeamCone.x, _BeamCone.y, emitterCos);
                if (beam <= 0) return float4(environment * _Exposure, 1);
                float3 irradiance = _LightRadiance.rgb * _LightRadiance.w * emitterCos / distanceSq;
                float3 diffuse = irradiance * saturate(dot(bodyNormal, lightDir)) * bodyColor * (1 - bodyF) / PI;
                float3 body = bodyF * _LightRadiance.rgb * RectangleSpecular(p, bodyRay, bodyAlpha);
                float mirror = RectangleSpecular(p, strikeRay, mirrorAlpha);
                float scratch = RectangleSpecular(p, strikeRay, ProjectScratchLobe(grain, scratchAlpha));
                float3 metal = strikeF * _LightRadiance.rgb * lerp(scratch, mirror, _MirrorShare);
                float3 radiance = environment + beam * lerp(diffuse + body, metal, polished);
                return float4(radiance * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}

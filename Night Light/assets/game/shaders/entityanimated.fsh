#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec2 uv;
in vec4 color;
in vec4 rgbaFog;
in float fogAmount;
in float glowLevel;
in vec3 vertexPosition;
flat in int renderFlags;
in vec3 normal;
in vec4 worldPos;
in vec3 blockLight;
in vec4 camPos;
in float damageEffect;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;
#if SSAOLEVEL > 0
in vec4 fragPosition;
in vec4 gnormal;
layout(location = 2) out vec4 outGNormal;
layout(location = 3) out vec4 outGPosition;
#endif


uniform sampler2D entityTex;
uniform float alphaTest = 0.001;
uniform float glitchEffectStrength;
uniform int entityId;
uniform int glitchFlicker;
uniform float frostAlpha = 0;



#include vertexflagbits.ash
#include fogandlight.fsh
#include noise3d.ash
#include noise2d.ash

void main () {
    vec4 mcolor = mix(color, vec4(1,1,1,color.a), MINIMUM_BRIGHTNESS_PERCENT);
    
	float b = 1;
	
	if (damageEffect > 0) {
		float f = cnoise2(floor(vec2(uv.x, uv.y) * 4096) / 4);
		if (f < damageEffect - 1.3) discard;
		b = min(1, f * 1.5 + 0.65 + (1-damageEffect));
	}

	vec4 texColor = texture(entityTex, uv);
	
	#if SHADOWQUALITY > 0
	float intensity = 0.34 + (1 - shadowIntensity)/8.0; // this was 0.45, which makes shadow acne visible on blocks
	#else
	float intensity = 0.45;
	#endif
	
	
	//float seed = mod(entityId, 1000) / 5.0; - this is broken on NVIDIA cards O_O
	int eidfloor = (entityId / 100) * 100;
	float seed = (entityId - eidfloor) / 5.0;
	
	texColor = applyFrostEffect(frostAlpha, texColor, normal, vertexPosition + vec3(seed));
	texColor = applyRustEffect(texColor, normal, vertexPosition + vec3(seed), 0);
	
	texColor *= mcolor;
	
	//texColor.rgb *= vec3(max(0, 1 - pow(vertexPosition.y / 6.8, 10))) * 0.9 + 0.1;
	//if (vertexPosition.y < 2) texColor.rgb *= vec3(max(0, 1 - pow(1 - vertexPosition.y / 6.5, 10))) * 0.9 + 0.1;
	
	outColor = applyFogAndShadowWithNormal(texColor, fogAmount, normal, 1, intensity);
	
	if (glitchFlicker >0 && glitchEffectStrength > 0) {
		float g = gnoise(vec3(gl_FragCoord.y / 2.0, gl_FragCoord.x / 2.0, windWaveCounter*30 + entityId * 3));
		outColor.a *= mix(1, clamp(0.7 + g / 2, 0, 1), glitchEffectStrength);
		
		float b = gnoise(vec3(0, 0, windWaveCounter*60 + entityId * 3));
		outColor.a *= mix(1, clamp(b * 10 + 2, 0, 1), glitchEffectStrength);
	}

#if NORMALVIEW == 0
	if (outColor.a < alphaTest) discard;
#endif



	float glow = 0;
#if SHINYEFFECT > 0	
	outColor = mix(applyReflectiveEffect(outColor, glow, renderFlags, uv, normal, worldPos, camPos, vec3(1)), outColor, min(1, 2 * fogAmount));
	//glow += pow(max(0, dot(normal, lightPosition)), 6) / 8 * shadowIntensity * (1 - fogAmount);
#endif

#if SSAOLEVEL > 0
	outGPosition = vec4(fragPosition.xyz, fogAmount + glowLevel);
	outGNormal = vec4(gnormal.xyz, 0);
#endif

#if NORMALVIEW > 0
	outColor = vec4((normal.x + 1) / 2, (normal.y + 1)/2, (normal.z+1)/2, 1);	
#endif

	outColor.rgb *= b;
	
	outGlow = vec4(glowLevel + glow, 0, 0, mcolor.a);
}
#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

uniform sampler2D terrainTex;
uniform sampler2D terrainTexLinear;
uniform float alphaTest;

in vec4 rgba;
in vec4 rgbaFog;
in float fogAmount;
in vec2 uv;
in float glowLevel;

flat in int renderFlags;
in vec3 normal;
in vec4 worldPos;
in vec3 vertexPosition;
in vec3 blockLight;
in vec4 gnormal;
in vec4 camPos;

uniform float fogDensityIn;
uniform float fogMinIn;
uniform float horizonFog;
uniform vec3 sunPosition;
uniform float dayLight;
uniform int haxyFade;


layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;
#if SSAOLEVEL > 0
layout(location = 2) out vec4 outGNormal;
layout(location = 3) out vec4 outGPosition;
#endif

#include vertexflagbits.ash
#include fogandlight.fsh
#include dither.fsh
#include skycolor.fsh
#include colormap.fsh

void main() 
{
    vec4 mrgba = mix(rgba, vec4(1,1,1,rgba.a), MINIMUM_BRIGHTNESS_PERCENT);
    
	// When looking through tinted glass you can clearly see the edges where we fade to sky color
	// Using this discard seems to completely fix that
#if NORMALVIEW == 0		
	if (mrgba.a < 0.005) discard;
#endif
	vec4 texColor = getColorMapped(terrainTexLinear, texture(terrainTex, uv)) * mrgba;
	
	texColor = applyRustEffect(texColor, normal, vertexPosition, 1);
	
	#if SHADOWQUALITY > 0
	float intensity = 0.34 + (1 - shadowIntensity)/8.0; // this was 0.45, which makes shadow acne visible on blocks
	#else
	float intensity = 0.45;
	#endif
	
	outColor = applyFogAndShadowWithNormal(texColor, fogAmount, normal, 1, intensity); 
	
	float glow = 0;
	
	
	// Fade to sky color
	float aTest = outColor.a;
	float godrayLevel = 0;
	if (mrgba.a < 0.999 && haxyFade > 0) {
		vec4 skyColor = vec4(1);
		vec4 skyGlow = vec4(1);
		float sealevelOffsetFactor = 0.25;
	
		getSkyColorAt(vec3(worldPos.x, worldPos.y + 0, worldPos.z), sunPosition, sealevelOffsetFactor, clamp(dayLight, 0, 1), horizonFog, skyColor, skyGlow);
		godrayLevel = skyGlow.g;
		outColor.rgb = mix(skyColor.rgb, outColor.rgb, max(1-dayLight, max(0, mrgba.a)));
	}
	
	// Lod 0 fade
	// This makes the lod fade more noticable, actually O_O
	if ((renderFlags & Lod0BitMask) != 0) {
		
		// We made this transition smoother, because it looks better,
		// if you notice chunk popping, revert to the old, harsher transition
		// Radfast and Tyron, May 28 2021 ^_^
		float b = clamp(10 * (1.05 - length(worldPos.xz) / viewDistanceLod0) - 2.5, 0, 1);
		//float b = clamp(20 * (1.05 - length(worldPos.xz) / viewDistanceLod0) - 5, 0, 1);
				
		aTest -= 1-b;
	}
	
	aTest += max(0, 1 - mrgba.a) * min(1, outColor.a * 10);
#if NORMALVIEW == 0	
	if (aTest < alphaTest) discard;
#endif


#if SHINYEFFECT > 0
	if ((renderFlags & ReflectiveBitMask) != 0) {
		float b = getBrightnessFromShadowMap();
		outColor = mix(applyReflectiveEffect(outColor, glow, renderFlags, uv, normal, worldPos, camPos, blockLight), outColor, clamp(2 * fogAmount + 2*(1-b), 0, 1));
	}
	glow += pow(max(0, dot(normal, lightPosition)), 6) / 8 * shadowIntensity * (1 - fogAmount);
#endif




	// Sparkly snow, not really working
	//if (skyExposed > 0)
	/*{
	
		vec3 lightDir = lightPosition;
		vec3 viewVec = normalize(camPos.xyz);
		vec3 pos = sadf.xyz;
		
		float specBase = clamp(dot(reflect(-viewVec, normal), lightDir), 0, 1);
		// Perturb a grid pattern with some noise and with the view-vector 
		// to let the glittering change with view.
		
		float noise = 9 * (gnoise(pos * 0.32) + gnoise(pos * 0.64) / 2 + gnoise(pos * 1.28) / 4 + gnoise(pos * 50) / 10.0);
		
		vec3 fp = fract(0.73 * pos + noise + 5 * viewVec); 
		fp *= (1 - fp);
		float glitter = clamp(1 - 2 * (fp.x + fp.y + fp.z), 0, 1);
		float sparkle = glitter * specBase;	
		
		float b = getBrightnessFromShadowMap();
		
		sparkle *= pow(b, 3) * max(0, gnoise(pos * 20));
		
		outColor.rgb += vec3(clamp((sparkle-0.05) * 6, 0,1));
	}*/




#if SSAOLEVEL > 0
	outGPosition = vec4(camPos.xyz, fogAmount * 2 + glowLevel);
	outGNormal = gnormal;
#endif

#if NORMALVIEW > 0
	outColor = vec4((normal.x + 1) / 2, (normal.y + 1)/2, (normal.z+1)/2, 1);	
#endif
	outGlow = vec4(glowLevel + glow, godrayLevel, 0, min(1, fogAmount + outColor.a));
	
//	outColor=vec4(1);
}

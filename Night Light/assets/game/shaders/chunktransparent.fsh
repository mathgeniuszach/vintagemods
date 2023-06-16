#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

uniform sampler2D terrainTex;

in vec4 rgba;
in vec4 rgbaFog;
in float fogAmount;
in vec2 uv;
in float glowLevel;
in vec4 worldPos;
in vec3 blockLight;

in float normalShadeIntensity;
flat in int renderFlags;
flat in vec3 normal;


layout(location = 0) out vec4 outAccu;
layout(location = 1) out vec4 outReveal;
layout(location = 2) out vec4 outGlow;

#include vertexflagbits.ash
#include fogandlight.fsh
#include noise3d.ash
#include colormap.fsh

void drawPixel(vec4 color) {
	float weight = color.a * clamp(0.03 / (1e-5 + pow(gl_FragCoord.z / 200, 4.0)), 1e-2, 3e3);
	
    // RGBA32F texture (accumulation)
    outAccu = vec4(color.rgb * color.a, color.a) * weight;
	
    // R32F texture (revealage)
    // Make sure to use the red channel (and GL_RED target in your texture)
    outReveal.r = color.a;

    outGlow = vec4(glowLevel, 0, 0, color.a);
}


void main() 
{
    vec4 mrgba = mix(rgba, vec4(1,1,1,rgba.a), MINIMUM_BRIGHTNESS_PERCENT);
    
	// When looking through tinted glass you can clearly see the edges where we fade to sky color
	// Using this discard seems to completely fix that
	if (mrgba.a < 0.005) discard;

	vec4 texColor = mrgba * getColorMapped(terrainTex, texture(terrainTex, uv));

	texColor = applyFogAndShadowWithNormal(texColor, fogAmount, normal, normalShadeIntensity, 0.45);

#if SHINYEFFECT > 0
	float glow=0;
	texColor = mix(applyReflectiveEffect(texColor, glow, renderFlags, uv, normal, worldPos, worldPos, blockLight), texColor, min(1, 2 * fogAmount));
#endif	

	drawPixel(texColor);
}
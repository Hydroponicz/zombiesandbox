// Full-screen injury feedback: red vignette, desaturation, pain blur, and fade to black on death.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float vignette : hint_range(0.0, 1.0) = 0.0;
uniform vec4 vignette_color : source_color = vec4(0.45, 0.0, 0.0, 1.0);
uniform float desaturate : hint_range(0.0, 1.0) = 0.0;
uniform float blur : hint_range(0.0, 3.0) = 0.0;
uniform float darken : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec3 color = textureLod(screen_texture, SCREEN_UV, blur).rgb;
	float gray = dot(color, vec3(0.299, 0.587, 0.114));
	color = mix(color, vec3(gray), desaturate);

	vec2 offset = (SCREEN_UV - 0.5) * vec2(1.25, 1.0);
	float edge = smoothstep(0.3, 0.85, length(offset));
	color = mix(color, vignette_color.rgb, clamp(edge * vignette * 1.4, 0.0, 0.92));

	color = mix(color, vec3(0.03, 0.0, 0.0), darken);
	COLOR = vec4(color, 1.0);
}

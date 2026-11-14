// Graybox material: 1 m world-space checker so speed and distance are easy to judge.
shader_type spatial;

uniform vec3 color_a : source_color = vec3(0.46, 0.46, 0.48);
uniform vec3 color_b : source_color = vec3(0.36, 0.36, 0.38);
uniform float tile_size = 1.0;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 cell = floor(world_position / tile_size + 0.001);
	float checker = mod(cell.x + cell.y + cell.z, 2.0);
	ALBEDO = mix(color_a, color_b, checker);
	ROUGHNESS = 0.9;
}

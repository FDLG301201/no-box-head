## One-shot generator for Assets/UI-interfaces/GameTheme.tres.
##
## Builds a Godot Theme resource from a hand-picked pill button (Pixel-UI-pack, the wide
## "LCD screen" style with a dark gradient centre and a coloured border ring) and saves it to
## disk, so project.godot's [gui] theme/custom can point at a real .tres (Godot's own resource
## system, not something hand-authored) and apply it to every Control in the game
## automatically — no per-scene wiring needed.
##
## Run:  Godot --headless --script res://Tools/generate_theme.gd
## Re-run any time Extracted/ assets change; the .tres is a build artifact, not source.
extends SceneTree

const EXTRACTED := "res://Assets/UI-interfaces/Extracted/"
const OUT_PATH := "res://Assets/UI-interfaces/GameTheme.tres"

# 9-slice margin (px, source resolution) — clears the rounded corner + border ring on the
# 48x22 source pill without eating into the dark centre that actually stretches.
const MARGIN := 6

# Only one state is baked per colour (no separate pressed/hover art this time), so the other
# button states are derived by tinting the same texture — brighter on hover, dimmer when
# pressed (reads as "pushed in"), desaturated/dark when disabled.
const HOVER_TINT    := Color(1.12, 1.12, 1.12)
const PRESSED_TINT  := Color(0.72, 0.72, 0.72)
const DISABLED_TINT := Color(0.5, 0.5, 0.5)

func _initialize():
	var theme := Theme.new()

	# Required or Godot silently falls back to the base "Button" style whenever a node sets
	# ThemeTypeVariation = "ButtonDanger" — registering the variation is what makes that
	# override actually resolve.
	theme.set_type_variation("ButtonDanger", "Button")

	_style_button(theme, "steel", "Button")        # primary actions (Solo Play, Resume, Connect…)
	_style_button(theme, "copper", "ButtonDanger")  # destructive / back (Back, Main Menu, Restart)

	theme.default_font_size = 18
	theme.set_color("font_color", "Label", Color(0.92, 0.92, 0.92))
	theme.set_color("font_color", "Button", Color(1, 1, 1))
	theme.set_color("font_color", "ButtonDanger", Color(1, 1, 1))

	var err := ResourceSaver.save(theme, OUT_PATH)
	print("Saved theme: ", OUT_PATH, "  err=", err)
	quit()

func _style_button(theme: Theme, color: String, type: String) -> void:
	theme.set_stylebox("normal", type, _make_stylebox(color, Color.WHITE))
	theme.set_stylebox("hover", type, _make_stylebox(color, HOVER_TINT))
	theme.set_stylebox("pressed", type, _make_stylebox(color, PRESSED_TINT))
	theme.set_stylebox("disabled", type, _make_stylebox(color, DISABLED_TINT))
	theme.set_font_size("font_size", type, 18)

func _make_stylebox(color: String, tint: Color) -> StyleBoxTexture:
	var sb := StyleBoxTexture.new()
	sb.texture = load("%sbtn_%s.png" % [EXTRACTED, color])
	sb.texture_margin_left = MARGIN
	sb.texture_margin_right = MARGIN
	sb.texture_margin_top = MARGIN
	sb.texture_margin_bottom = MARGIN
	sb.content_margin_left = 14
	sb.content_margin_right = 14
	sb.content_margin_top = 6
	sb.content_margin_bottom = 6
	sb.axis_stretch_horizontal = StyleBoxTexture.AXIS_STRETCH_MODE_STRETCH
	sb.axis_stretch_vertical = StyleBoxTexture.AXIS_STRETCH_MODE_STRETCH
	sb.modulate_color = tint
	return sb

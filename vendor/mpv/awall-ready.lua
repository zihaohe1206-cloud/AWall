-- playback-restart fires after initial loading/seeking has reached playback.
mp.register_event('playback-restart', function()
    mp.set_property_bool('user-data/awall-ready', true)
end)

-- Fieldlog measurement store.
--
-- Design notes:
--   * Readings reference channels by their catalog key (text), matching config/channels.json.
--     There is no surrogate-id lookup on the write path.
--   * taken_at is stored as timestamptz; the application always supplies UTC instants.
--   * quality mirrors Fieldlog.Core.Acquisition.ReadingQuality in lower case.

CREATE TABLE IF NOT EXISTS channel (
    channel_key  text PRIMARY KEY,
    unit         text NOT NULL DEFAULT '',
    description  text NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS measurement (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    channel_key  text        NOT NULL REFERENCES channel (channel_key),
    taken_at     timestamptz NOT NULL,
    reading      double precision NOT NULL,
    quality      text        NOT NULL DEFAULT 'good'
                 CHECK (quality IN ('good', 'uncertain', 'bad'))
);

-- Time-range queries per channel are the dominant access pattern.
CREATE INDEX IF NOT EXISTS ix_measurement_channel_time
    ON measurement (channel_key, taken_at);

-- Seed rows for the channels shipped in config/channels.json.
INSERT INTO channel (channel_key, unit, description) VALUES
    ('ambient_temp',     '°C',  'Ambient temperature at the machine'),
    ('feed_water_temp',  '°C',  'Feed water temperature'),
    ('slurry_temp',      '°C',  'Slurry temperature in the mixing zone'),
    ('feed_water_flow',  'L/h', 'Feed water flow rate'),
    ('rotor_speed',      'rpm', 'Rotor speed of the conveying unit'),
    ('reversal_counter', 's',   'Accumulated reversal runtime'),
    ('drive_power',      'W',   'Active power drawn by the drive'),
    ('line_pressure',    'bar', 'Delivery line pressure')
ON CONFLICT (channel_key) DO NOTHING;

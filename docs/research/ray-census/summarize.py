# Tool code of ter-8pco: prints one take's census-result.json as compact rows.
import json, sys
r = json.load(open(sys.argv[1]))
f = lambda d: ' '.join('%s %.3f' % (k, v) for k, v in d.items())
print(r['case'], r['render'], 'stops %.3f to %.3f' % tuple(r['stops']))
for g in r['regions']:
    a, b = g['after_caps'], g['before_caps']
    print(' ', g['name'], '| mean:', f(a['mean_share']), '| var:', f(a['variance_share']), '| var pre-cap:',
          f(b['variance_share']), '| check %.3f' % g['check']['ratio'])
    for key in ('brightest_0.1pct_x', 'brightest_0.1pct_pre'):
        if key in g:
            print('    ', key, g[key]['samples'], 'threshold %.3f' % g[key]['threshold'], f(g[key]['share_of_their_light']),
                  'of all light %.3f' % g[key]['share_of_all_light_in_rect'])

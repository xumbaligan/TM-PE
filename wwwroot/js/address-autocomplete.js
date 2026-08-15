///*
// * Address autocomplete for plain <input> text fields.
// *
// * Uses the free Photon geocoding API (https://photon.komoot.io), which is
// * built on OpenStreetMap data and requires no API key. As the user types,
// * matching address suggestions are shown in a dropdown below the input;
// * picking one fills the input with the formatted address.
// *
// * Usage:
// *   initAddressAutocomplete(document.getElementById('myInput'));
// *
// * Optional second argument:
// *   initAddressAutocomplete(inputEl, {
// *       lat: 10.3157, lon: 123.8854,  // bias results near this point
// *       minLength: 3,                 // min characters before searching
// *       limit: 6                      // max suggestions shown
// *   });
// */
//(function (window) {
//    'use strict';

//    function debounce(fn, wait) {
//        let timer = null;
//        return function (...args) {
//            clearTimeout(timer);
//            timer = setTimeout(() => fn.apply(this, args), wait);
//        };
//    }

//    // Builds a readable "Street, City, Province, Country" style label out of
//    // whatever fields Photon returned for a given result.
//    function formatSuggestion(props) {
//        const parts = [];

//        const streetBits = [props.housenumber, props.street].filter(Boolean).join(' ');
//        if (streetBits) parts.push(streetBits);
//        else if (props.name) parts.push(props.name);

//        if (props.district && props.district !== props.name) parts.push(props.district);
//        if (props.city) parts.push(props.city);
//        else if (props.county) parts.push(props.county);

//        if (props.state) parts.push(props.state);
//        if (props.country) parts.push(props.country);

//        // De-duplicate consecutive/repeated parts (Photon sometimes repeats
//        // the same value across fields, e.g. name === city).
//        const seen = new Set();
//        const unique = parts.filter(p => {
//            const key = p.trim().toLowerCase();
//            if (seen.has(key)) return false;
//            seen.add(key);
//            return true;
//        });

//        return unique.join(', ');
//    }

//    function initAddressAutocomplete(input, options) {
//        if (!input || input.dataset.autocompleteInit === 'true') return;
//        input.dataset.autocompleteInit = 'true';

//        const opts = Object.assign({
//            minLength: 3,
//            limit: 6,
//            lat: null,
//            lon: null
//        }, options || {});

//        // Make sure the input has a positioning context to anchor the
//        // dropdown to, without disturbing existing layout/classes.
//        const wrapper = document.createElement('div');
//        wrapper.style.position = 'relative';
//        input.parentNode.insertBefore(wrapper, input);
//        wrapper.appendChild(input);
//        input.setAttribute('autocomplete', 'off');

//        const list = document.createElement('div');
//        list.className = 'address-autocomplete-list';
//        Object.assign(list.style, {
//            position: 'absolute',
//            top: '100%',
//            left: '0',
//            right: '0',
//            zIndex: '1050',
//            background: '#fff',
//            border: '1px solid #ced4da',
//            borderTop: 'none',
//            borderRadius: '0 0 .375rem .375rem',
//            boxShadow: '0 4px 10px rgba(0,0,0,0.08)',
//            maxHeight: '260px',
//            overflowY: 'auto',
//            display: 'none'
//        });
//        wrapper.appendChild(list);

//        let currentResults = [];
//        let activeIndex = -1;
//        let abortController = null;

//        function hideList() {
//            list.style.display = 'none';
//            list.innerHTML = '';
//            activeIndex = -1;
//        }

//        function renderList() {
//            list.innerHTML = '';

//            if (currentResults.length === 0) {
//                hideList();
//                return;
//            }

//            currentResults.forEach((label, idx) => {
//                const item = document.createElement('div');
//                item.textContent = label;
//                item.className = 'address-autocomplete-item';
//                Object.assign(item.style, {
//                    padding: '.5rem .75rem',
//                    cursor: 'pointer',
//                    fontSize: '.9rem',
//                    borderBottom: idx === currentResults.length - 1 ? 'none' : '1px solid #f1f1f1'
//                });
//                item.addEventListener('mouseenter', () => setActive(idx));
//                item.addEventListener('mousedown', (e) => {
//                    // mousedown (not click) so it fires before the input's blur
//                    e.preventDefault();
//                    selectSuggestion(idx);
//                });
//                list.appendChild(item);
//            });

//            list.style.display = 'block';
//        }

//        function setActive(idx) {
//            activeIndex = idx;
//            Array.from(list.children).forEach((child, i) => {
//                child.style.background = i === idx ? '#f0f4ff' : '#fff';
//            });
//        }

//        function selectSuggestion(idx) {
//            const label = currentResults[idx];
//            if (!label) return;
//            input.value = label;
//            hideList();
//            input.dispatchEvent(new Event('change', { bubbles: true }));
//        }

//        const fetchSuggestions = debounce(function (query) {
//            if (abortController) abortController.abort();
//            abortController = new AbortController();

//            const params = new URLSearchParams({
//                q: query,
//                limit: String(opts.limit),
//                lang: 'en'
//            });
//            if (opts.lat != null && opts.lon != null) {
//                params.set('lat', String(opts.lat));
//                params.set('lon', String(opts.lon));
//            }

//            fetch('https://photon.komoot.io/api/?' + params.toString(), { signal: abortController.signal })
//                .then(res => res.ok ? res.json() : Promise.reject(res))
//                .then(data => {
//                    const features = Array.isArray(data.features) ? data.features : [];
//                    currentResults = features
//                        .map(f => formatSuggestion(f.properties || {}))
//                        .filter((label, idx, arr) => label && arr.indexOf(label) === idx);
//                    renderList();
//                })
//                .catch(err => {
//                    if (err && err.name === 'AbortError') return;
//                    hideList();
//                });
//        }, 350);

//        input.addEventListener('input', function () {
//            const query = input.value.trim();
//            if (query.length < opts.minLength) {
//                hideList();
//                return;
//            }
//            fetchSuggestions(query);
//        });

//        input.addEventListener('keydown', function (e) {
//            if (list.style.display === 'none' || currentResults.length === 0) return;

//            if (e.key === 'ArrowDown') {
//                e.preventDefault();
//                setActive(Math.min(activeIndex + 1, currentResults.length - 1));
//            } else if (e.key === 'ArrowUp') {
//                e.preventDefault();
//                setActive(Math.max(activeIndex - 1, 0));
//            } else if (e.key === 'Enter') {
//                if (activeIndex >= 0) {
//                    e.preventDefault();
//                    selectSuggestion(activeIndex);
//                }
//            } else if (e.key === 'Escape') {
//                hideList();
//            }
//        });

//        input.addEventListener('blur', function () {
//            // slight delay so a mousedown-selection isn't cancelled by blur
//            setTimeout(hideList, 100);
//        });

//        document.addEventListener('click', function (e) {
//            if (!wrapper.contains(e.target)) hideList();
//        });
//    }

//    window.initAddressAutocomplete = initAddressAutocomplete;
//})(window);
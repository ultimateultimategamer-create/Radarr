import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { sortDirections } from 'Helpers/Props';
import MovieVersionHeaderRow from 'Movie/Details/Versions/MovieVersionHeaderRow';
import translate from 'Utilities/String/translate';
import MovieFileEditorRow from './MovieFileEditorRow';
import styles from './MovieFileEditorTableContent.css';

function hasMatchingRules(version) {
  return !!version.requiredCustomFormatIds?.length;
}

// The primary version gets a header, with its editor, while it has matching rules or automatic versions depend on it.
// Its rules keep filtering releases, so they must stay editable.
function needsPrimaryHeader(versions) {
  return versions.some((version) => {
    return version.isPrimary ? hasMatchingRules(version) : !version.pinned;
  });
}

// A movie without additional versions or primary version rules looks exactly as usual
function needsVersionGroups(versions) {
  return versions.some((version) => !version.isPrimary) || needsPrimaryHeader(versions);
}

// The movie's own files are listed as usual, each additional version follows with a header and its file.
// Files carry the id of their additional version, 0 for the movie's primary version.
function groupFilesByVersion(items, versions) {
  const primary = versions.find((version) => version.isPrimary);
  const additionalVersions = versions.filter((version) => !version.isPrimary);
  const additionalIds = additionalVersions.map((version) => version.id);

  return {
    primary: primary && needsPrimaryHeader(versions) ? primary : null,
    primaryFiles: items.filter((item) => !additionalIds.includes(item.movieVersionId ?? 0)),
    versionGroups: additionalVersions.map((version) => {
      return {
        version,
        files: items.filter((item) => item.movieVersionId === version.id)
      };
    })
  };
}

class MovieFileEditorTableContent extends Component {

  //
  // Render

  renderRow = (item) => {
    return (
      <MovieFileEditorRow
        key={item.id}
        columns={this.props.columns}
        {...item}
        onDeleteMovieFile={this.props.onDeletePress}
      />
    );
  };

  render() {
    const {
      items,
      versions,
      columns,
      sortKey,
      sortDirection,
      onSortPress,
      onTableOptionChange,
      onRemoveVersionPress
    } = this.props;

    const groups = versions && needsVersionGroups(versions) ?
      groupFilesByVersion(items, versions) :
      null;

    const colSpan = columns.filter((column) => column.isVisible).length;

    return (
      <div>
        {
          !items.length && !groups &&
            <div className={styles.blankpad}>
              {translate('NoMovieFilesToManage')}
            </div>
        }

        {
          (!!items.length || !!groups) &&
            <Table
              columns={columns}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortPress={onSortPress}
              onTableOptionChange={onTableOptionChange}
            >
              <TableBody>
                {
                  groups ?
                    [
                      groups.primary ?
                        <MovieVersionHeaderRow
                          key={`version-${groups.primary.id}`}
                          version={groups.primary}
                          colSpan={colSpan}
                          onRemovePress={onRemoveVersionPress}
                        /> :
                        null,
                      ...groups.primaryFiles.map(this.renderRow),
                      ...groups.versionGroups.map(({ version, files }) => {
                        return [
                          <MovieVersionHeaderRow
                            key={`version-${version.id}`}
                            version={version}
                            colSpan={colSpan}
                            onRemovePress={onRemoveVersionPress}
                          />,
                          ...files.map(this.renderRow)
                        ];
                      })
                    ] :
                    items.map(this.renderRow)
                }
              </TableBody>
            </Table>
        }

      </div>
    );
  }
}

MovieFileEditorTableContent.propTypes = {
  movieId: PropTypes.number,
  isDeleting: PropTypes.bool.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  sortKey: PropTypes.string.isRequired,
  sortDirection: PropTypes.oneOf(sortDirections.all),
  onTableOptionChange: PropTypes.func.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onDeletePress: PropTypes.func.isRequired,
  versions: PropTypes.arrayOf(PropTypes.object),
  onRemoveVersionPress: PropTypes.func
};

export default MovieFileEditorTableContent;
